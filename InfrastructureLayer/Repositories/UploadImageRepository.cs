using DomainLayer.Interface;
using Umbraco.Cms.Core.Services;

namespace InfrastructureLayer.Repositories
{
    public class UploadImageRepository : IUploadImageRepopsitory
    {
        private readonly IMediaService _mediaService;

        public UploadImageRepository(IMediaService mediaService)
        {
            _mediaService = mediaService ?? throw new ArgumentNullException(nameof(mediaService));
        }

        public int? UploadAndGetImageId(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath))
            {
                Console.WriteLine("Image path is null or empty.");
                return null;
            }

            FileInfo fileInfo = new FileInfo(imagePath);
            string fileName = fileInfo.Name;
            string extension = fileInfo.Extension.ToLower();

            string[] allowedExtensions = { ".jpg", ".jpeg", ".png", ".webp" };

            if (!allowedExtensions.Contains(extension))
            {
                Console.WriteLine("Invalid image format. Allowed formats: JPG, PNG, WEBP.");
                return null;
            }

            var mediaItem = _mediaService.CreateMedia(fileName, -1, "Image");

            using (var stream = System.IO.File.OpenRead(imagePath))
            {
                string mediaPath = $"media/{fileName}";
                string fullMediaPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", mediaPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullMediaPath));

                using (var fileStream = new FileStream(fullMediaPath, FileMode.Create, FileAccess.Write))
                {
                    stream.CopyTo(fileStream);
                }

                mediaItem.SetValue("umbracoFile", $"/{mediaPath}");
                _mediaService.Save(mediaItem);

                return mediaItem.Id;
            }
        }
    }


}
