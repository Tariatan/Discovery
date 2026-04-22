using System.Drawing.Imaging;
using OpenCvSharp;

namespace Discovery.Tests;

internal static class SyntheticDiscoveryImageFactory
{
    private const int ImageWidth = 1200;
    private const int ImageHeight = 900;
    private const int PlayfieldLeft = 100;
    private const int PlayfieldTop = 150;
    private const int PlayfieldRight = 800;
    private const int PlayfieldBottom = 800;

    public static Mat CreateSingleClusterImage()
    {
        return CreateImage(includeSecondCluster: false);
    }

    public static Mat CreateTwoClusterImage()
    {
        return CreateImage(includeSecondCluster: true);
    }

    public static Mat CreateFourClusterImage()
    {
        return CreateImage(
            new ClusterDefinition(new OpenCvSharp.Point(280, 300), new OpenCvSharp.Size(85, 55), new Scalar(0, 120, 255)),
            new ClusterDefinition(new OpenCvSharp.Point(415, 325), new OpenCvSharp.Size(78, 52), new Scalar(0, 190, 255)),
            new ClusterDefinition(new OpenCvSharp.Point(320, 505), new OpenCvSharp.Size(76, 54), new Scalar(255, 180, 0)),
            new ClusterDefinition(new OpenCvSharp.Point(515, 520), new OpenCvSharp.Size(72, 52), new Scalar(255, 220, 0)));
    }

    public static Mat CreateSparseLowerClusterImage()
    {
        var image = CreateImage(includeSecondCluster: false);
        DrawSparseCluster(image, new OpenCvSharp.Point(360, 650), new OpenCvSharp.Size(155, 58), new Scalar(255, 185, 0), 340);
        return image;
    }

    public static Mat CreateMultiSizeClusterImage()
    {
        return CreateImage(
            new ClusterDefinition(new OpenCvSharp.Point(250, 245), new OpenCvSharp.Size(95, 55), new Scalar(0, 180, 255)),
            new ClusterDefinition(new OpenCvSharp.Point(520, 250), new OpenCvSharp.Size(42, 36), new Scalar(0, 210, 255)),
            new ClusterDefinition(new OpenCvSharp.Point(310, 470), new OpenCvSharp.Size(115, 82), new Scalar(255, 170, 0)),
            new ClusterDefinition(new OpenCvSharp.Point(445, 420), new OpenCvSharp.Size(36, 34), new Scalar(255, 220, 0)),
            new ClusterDefinition(new OpenCvSharp.Point(560, 540), new OpenCvSharp.Size(58, 90), new Scalar(255, 200, 40)));
    }

    public static void WriteSingleClusterImage(string outputPath)
    {
        using var image = CreateSingleClusterImage();
        Cv2.ImWrite(outputPath, image);
    }

    public static void WriteTwoClusterImage(string outputPath)
    {
        using var image = CreateTwoClusterImage();
        Cv2.ImWrite(outputPath, image);
    }

    public static void WriteFourClusterImage(string outputPath)
    {
        using var image = CreateFourClusterImage();
        Cv2.ImWrite(outputPath, image);
    }

    public static void WriteSparseLowerClusterImage(string outputPath)
    {
        using var image = CreateSparseLowerClusterImage();
        Cv2.ImWrite(outputPath, image);
    }

    public static void WriteMultiSizeClusterImage(string outputPath)
    {
        using var image = CreateMultiSizeClusterImage();
        Cv2.ImWrite(outputPath, image);
    }

    private static Mat CreateImage(bool includeSecondCluster)
    {
        var clusters = new List<ClusterDefinition>
        {
            new(new OpenCvSharp.Point(330, 430), new OpenCvSharp.Size(110, 70), new Scalar(0, 120, 255)),
            new(new OpenCvSharp.Point(315, 420), new OpenCvSharp.Size(55, 35), new Scalar(0, 200, 255))
        };

        if (includeSecondCluster)
        {
            clusters.Add(new ClusterDefinition(new OpenCvSharp.Point(520, 580), new OpenCvSharp.Size(70, 50), new Scalar(255, 180, 0)));
        }

        return CreateImage(clusters.ToArray());
    }

    private static Mat CreateImage(params ClusterDefinition[] clusters)
    {
        var image = new Mat(new OpenCvSharp.Size(ImageWidth, ImageHeight), MatType.CV_8UC3, Scalar.All(0));

        using var marker = LoadMarkerImage();
        PasteMarker(image, marker, new OpenCvSharp.Point(PlayfieldLeft, PlayfieldTop));
        PasteMarker(image, marker, new OpenCvSharp.Point(PlayfieldRight - marker.Width, PlayfieldTop));
        PasteMarker(image, marker, new OpenCvSharp.Point(PlayfieldLeft, PlayfieldBottom - marker.Height));
        PasteMarker(image, marker, new OpenCvSharp.Point(PlayfieldRight - marker.Width, PlayfieldBottom - marker.Height));

        foreach (var cluster in clusters)
        {
            DrawCluster(image, cluster.Center, cluster.Size, cluster.Color);
        }

        return image;
    }

    private static Mat LoadMarkerImage()
    {
        using var bitmap = Discovery.Properties.Resources.marker;
        using var memoryStream = new MemoryStream();
        bitmap.Save(memoryStream, ImageFormat.Png);
        return Cv2.ImDecode(memoryStream.ToArray(), ImreadModes.Color);
    }

    private static void PasteMarker(Mat image, Mat marker, OpenCvSharp.Point location)
    {
        using var roi = new Mat(image, new Rect(location.X, location.Y, marker.Width, marker.Height));
        marker.CopyTo(roi);
    }

    private static void DrawCluster(Mat image, OpenCvSharp.Point center, OpenCvSharp.Size size, Scalar color)
    {
        Cv2.Ellipse(image, center, size, 0, 0, 360, color, -1, LineTypes.AntiAlias);
    }

    private static void DrawSparseCluster(Mat image, OpenCvSharp.Point center, OpenCvSharp.Size spread, Scalar color, int count)
    {
        var random = new Random(12345);
        for (var index = 0; index < count; index++)
        {
            var radiusX = (random.NextDouble() * 2.0) - 1.0;
            var radiusY = (random.NextDouble() * 2.0) - 1.0;
            var x = center.X + (int)Math.Round(radiusX * spread.Width);
            var y = center.Y + (int)Math.Round(radiusY * spread.Height);
            var point = new OpenCvSharp.Point(
                Math.Clamp(x, 0, image.Width - 1),
                Math.Clamp(y, 0, image.Height - 1));
            Cv2.Circle(image, point, 1, color, -1, LineTypes.AntiAlias);
        }
    }

    private readonly record struct ClusterDefinition(OpenCvSharp.Point Center, OpenCvSharp.Size Size, Scalar Color);
}
