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

    private static Mat CreateImage(bool includeSecondCluster)
    {
        var image = new Mat(new OpenCvSharp.Size(ImageWidth, ImageHeight), MatType.CV_8UC3, Scalar.All(0));

        using var marker = LoadMarkerImage();
        PasteMarker(image, marker, new OpenCvSharp.Point(PlayfieldLeft, PlayfieldTop));
        PasteMarker(image, marker, new OpenCvSharp.Point(PlayfieldRight - marker.Width, PlayfieldTop));
        PasteMarker(image, marker, new OpenCvSharp.Point(PlayfieldLeft, PlayfieldBottom - marker.Height));
        PasteMarker(image, marker, new OpenCvSharp.Point(PlayfieldRight - marker.Width, PlayfieldBottom - marker.Height));

        DrawCluster(image, new OpenCvSharp.Point(330, 430), new OpenCvSharp.Size(110, 70), new Scalar(0, 120, 255));
        DrawCluster(image, new OpenCvSharp.Point(315, 420), new OpenCvSharp.Size(55, 35), new Scalar(0, 200, 255));

        if (includeSecondCluster)
        {
            DrawCluster(image, new OpenCvSharp.Point(520, 580), new OpenCvSharp.Size(70, 50), new Scalar(255, 180, 0));
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
}
