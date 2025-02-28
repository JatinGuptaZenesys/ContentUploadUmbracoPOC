namespace DomainLayer.Interface
{
    public interface IUploadImageRepopsitory
    {
        int? UploadAndGetImageId(string imagePath);
    }
}
