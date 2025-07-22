using Minio;
using Minio.DataModel.Args;

namespace IngestaArchivosAPI.Services;

public class MinioService
{
    private readonly IMinioClient _client;
    private readonly string _bucketName = "archivos-ingestados";

    public MinioService(IMinioClient client)
    {
        _client = client;
    }

    public IMinioClient Client => _client;

    public async Task SubirArchivoAsync(string ruta, Stream contenido, long tamano, string contentType)
    {
        await _client.PutObjectAsync(new PutObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(ruta)
            .WithStreamData(contenido)
            .WithObjectSize(tamano)
            .WithContentType(contentType));
    }

    public async Task<Stream> ObtenerArchivoAsync(string ruta)
    {
        var memoryStream = new MemoryStream();
        
        await _client.GetObjectAsync(new GetObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(ruta)
            .WithCallbackStream(stream => stream.CopyTo(memoryStream)));
        
        memoryStream.Position = 0;
        return memoryStream;
    }
}
