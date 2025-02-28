namespace DomainLayer.Interface
{
    public interface IContentUploadRepository
    {
        string ImportContentWithImage(List<string> filePath,List<string>allowedImageTypes);
    }
}
