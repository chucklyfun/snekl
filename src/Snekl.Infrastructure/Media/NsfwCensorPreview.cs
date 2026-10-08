using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Snekl.Infrastructure.Media;

public static class NsfwCensorPreview
{
    public const int MaxEdge = 48;

    public static byte[]? TryCreate(Stream input)
    {
        try
        {
            input.Position = 0;
            using var image = Image.Load(input);
            image.Mutate(ctx =>
            {
                ctx.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(MaxEdge, MaxEdge),
                    Sampler = KnownResamplers.Box
                });
            });

            if (image.Width < 8 || image.Height < 8)
            {
                image.Mutate(ctx => ctx.Resize(MaxEdge, MaxEdge, KnownResamplers.NearestNeighbor));
            }

            image.Mutate(ctx => ctx.GaussianBlur(3.2f));

            using var output = new MemoryStream();
            image.SaveAsJpeg(output, new JpegEncoder { Quality = 35 });
            return output.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
