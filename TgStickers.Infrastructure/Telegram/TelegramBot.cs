using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Flurl.Http;

namespace TgStickers.Infrastructure.Telegram
{
    public class TelegramBot
    {
        private const string GetStickerPackUrl = "https://api.telegram.org/bot{0}/getStickerSet?name={1}";
        private const string GetFileUrl = "https://api.telegram.org/bot{0}/getFile?file_id={1}";
        private const string GetFileBlobUrl = "https://api.telegram.org/file/bot{0}/{1}?file_id={2}";
        private const string GetUpdatesUrl = "https://api.telegram.org/bot{0}/getUpdates";
        private const string SendMessageUrl = "https://api.telegram.org/bot{0}/sendMessage?chat_id={1}&text={2}";

        private readonly string _botToken;
        private readonly string _directoryForImages;

        public TelegramBot(string botToken, string directoryForImages)
        {
            _botToken = botToken;
            _directoryForImages = directoryForImages;
        }

        public async Task<bool> IsStickerPackExistsAsync(string stickerPackName)
        {
            try
            {
                await GetStickerFilesFromPackAsync(stickerPackName);
            }
            catch (FlurlHttpException ex)
            {
                // Exactly 400, not 404. That's telegram's api ¯\_(ツ)_/¯
                if (ex.Call.HttpStatus == HttpStatusCode.BadRequest)
                {
                    return false;
                }

                throw;
            }

            return true;
        }

        public async Task<IEnumerable<string>> GetStickerFilesFromPackAsync(string stickerPackName)
        {
            var stickerPack = await string.Format(GetStickerPackUrl, _botToken, stickerPackName).GetJsonAsync();
            var stickers = (IEnumerable<dynamic>) stickerPack.result.stickers;

            return stickers.Select(sticker => (string) sticker.thumb.file_id);
        }

        public async Task<string> GetFilePathAsync(string stickerPackName, string fileId)
        {
            var fileFullPath = GetFormattedFilename(stickerPackName, fileId, _directoryForImages);
            var filePath = GetFormattedFilename(stickerPackName, fileId, baseDirectory: null);

            if (File.Exists(fileFullPath))
            {
                return filePath;
            }

            Directory.CreateDirectory(path: $"{_directoryForImages}/{stickerPackName}");

            await SaveFileAsync(fileFullPath, await DownloadFileAsync(fileId));

            return filePath;
        }

        public async Task SendMessageAsync(string receiver, string content)
        {
            var updates = (IEnumerable<dynamic>) (await string.Format(GetUpdatesUrl, _botToken).GetJsonAsync()).result;

            var chatId = updates.First(update =>
            {
                try
                {
                    return update.message.chat.username == receiver;
                }
                catch
                {
                    return false;
                }
            }).message.chat.id;

            await (string.Format(SendMessageUrl, _botToken, Convert.ToString(chatId), content) as string).GetAsync();
        }

        private async Task<Stream> DownloadFileAsync(string fileId)
        {
            var filePath = await GetFilePathAsync(fileId);

            return await string.Format(GetFileBlobUrl, _botToken, filePath, fileId).GetStreamAsync();
        }

        private async Task<string> GetFilePathAsync(string fileId)
        {
            var file = await string.Format(GetFileUrl, _botToken, fileId).GetJsonAsync();

            return file.result.file_path;
        }

        private static async Task SaveFileAsync(string filePath, Stream blobStream)
        {
            await using var fileStream = new FileStream(filePath, FileMode.Create);

            await blobStream.CopyToAsync(fileStream);
            await fileStream.FlushAsync();
        }

        public static string GetFormattedFilename(string stickerPackName, string fileId, string? baseDirectory = null)
        {
            return string.IsNullOrEmpty(baseDirectory)
                ? $"{stickerPackName}/{fileId}.webp"
                : $"{baseDirectory}/{stickerPackName}/{fileId}.webp";
        }
    }
}
