using Minio;
using Minio.ApiEndpoints;
using Minio.DataModel;
using Minio.DataModel.Args;

namespace IngestaArchivosAPI.Utils;

public class MinioUtils
{
    IMinioClient _minioClient;

    public MinioUtils(IMinioClient minioClient)
    {
        _minioClient = minioClient;
    }

    public async Task<List<string>> GetObjectsAsync(string bucketName)
    {
        var objectNames = new List<string>();

        var objectItems = _minioClient.ListObjectsEnumAsync(
            new ListObjectsArgs()
                .WithBucket(bucketName)
                .WithRecursive(true)
        );

        await foreach (Item item in objectItems)
        {
            objectNames.Add(item.Key);
        }

        return objectNames;
    }

    public async Task<byte[]> GetObjectContentAsync(string bucketName, string objectName)
    {
        using var memoryStream = new MemoryStream();
        await _minioClient.GetObjectAsync(
            new GetObjectArgs()
                .WithBucket(bucketName)
                .WithObject(objectName)
                .WithCallbackStream(stream => stream.CopyTo(memoryStream))
        );

        return memoryStream.ToArray();
    }

    public async Task MoverArchivoEntreBucketsAsync(string bucketOrigen, string bucketDestino, string objectName)
    {
        // Descargar el archivo del bucket origen
        var contenido = await GetObjectContentAsync(bucketOrigen, objectName);

        // Subir el archivo al bucket destino
        using (var stream = new MemoryStream(contenido))
        {
            await _minioClient.PutObjectAsync(
                new PutObjectArgs()
                    .WithBucket(bucketDestino)
                    .WithObject(objectName)
                    .WithStreamData(stream)
                    .WithObjectSize(stream.Length)
            );
        }

        // Eliminar el archivo del bucket origen
        await _minioClient.RemoveObjectAsync(
            new RemoveObjectArgs()
                .WithBucket(bucketOrigen)
                .WithObject(objectName)
        );
    }
}
