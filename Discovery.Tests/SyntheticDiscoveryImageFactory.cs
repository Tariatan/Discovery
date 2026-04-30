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

    public static Mat CreateMaximumSubmissionsPopupImage()
    {
        var image = new Mat(new OpenCvSharp.Size(1701, 1345), MatType.CV_8UC3, new Scalar(22, 28, 30));
        DrawMaximumSubmissionsPopup(image);
        return image;
    }

    public static Mat CreateMaximumSubmissionsPopupImageWithPlayfield()
    {
        var image = CreateMaximumSubmissionsPopupImage();
        using var marker = LoadMarkerImage();
        PasteMarker(image, marker, new OpenCvSharp.Point(PlayfieldLeft, PlayfieldTop));
        PasteMarker(image, marker, new OpenCvSharp.Point(PlayfieldRight - marker.Width, PlayfieldTop));
        PasteMarker(image, marker, new OpenCvSharp.Point(PlayfieldLeft, PlayfieldBottom - marker.Height));
        PasteMarker(image, marker, new OpenCvSharp.Point(PlayfieldRight - marker.Width, PlayfieldBottom - marker.Height));
        DrawCluster(image, new OpenCvSharp.Point(330, 430), new OpenCvSharp.Size(110, 70), new Scalar(0, 120, 255));
        DrawCluster(image, new OpenCvSharp.Point(315, 420), new OpenCvSharp.Size(55, 35), new Scalar(0, 200, 255));
        return image;
    }

    public static Mat CreateWideScreenMaximumSubmissionsPopupImage()
    {
        var image = new Mat(new OpenCvSharp.Size(3000, 1600), MatType.CV_8UC3, new Scalar(12, 14, 16));
        using var popupImage = CreateMaximumSubmissionsPopupImage();
        using var region = new Mat(image, new Rect(120, 20, popupImage.Width, popupImage.Height));
        popupImage.CopyTo(region);
        return image;
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

    public static void WriteMaximumSubmissionsPopupImage(string outputPath)
    {
        using var image = CreateMaximumSubmissionsPopupImage();
        Cv2.ImWrite(outputPath, image);
    }

    public static void WriteMaximumSubmissionsPopupImageWithPlayfield(string outputPath)
    {
        using var image = CreateMaximumSubmissionsPopupImageWithPlayfield();
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

    private static void DrawMaximumSubmissionsPopup(Mat image)
    {
        var popup = new Rect(
            (int)Math.Round(image.Width * 0.56),
            (int)Math.Round(image.Height * 0.62),
            (int)Math.Round(image.Width * 0.36),
            (int)Math.Round(image.Height * 0.29));
        Cv2.Rectangle(image, popup, new Scalar(7, 7, 7), -1);
        Cv2.Rectangle(image, popup, new Scalar(75, 65, 45), 1);

        var iconCenter = new OpenCvSharp.Point(
            popup.X + (int)Math.Round(popup.Width * 0.10),
            popup.Y + (int)Math.Round(popup.Height * 0.16));
        Cv2.Circle(image, iconCenter, 23, new Scalar(235, 235, 235), -1, LineTypes.AntiAlias);
        Cv2.PutText(
            image,
            "i",
            new OpenCvSharp.Point(iconCenter.X - 5, iconCenter.Y + 11),
            HersheyFonts.HersheyDuplex,
            1.0,
            new Scalar(30, 30, 30),
            2,
            LineTypes.AntiAlias);

        var titleLeft = popup.X + (int)Math.Round(popup.Width * 0.20);
        var titleTop = popup.Y + (int)Math.Round(popup.Height * 0.12);
        Cv2.PutText(image, "Maximum Number of", new OpenCvSharp.Point(titleLeft, titleTop), HersheyFonts.HersheySimplex, 1.25, Scalar.All(235), 3, LineTypes.AntiAlias);
        Cv2.PutText(image, "Submissions Reached", new OpenCvSharp.Point(titleLeft, titleTop + 48), HersheyFonts.HersheySimplex, 1.25, Scalar.All(235), 3, LineTypes.AntiAlias);

        var bodyLeft = popup.X + (int)Math.Round(popup.Width * 0.04);
        var bodyTop = popup.Y + (int)Math.Round(popup.Height * 0.36);
        Cv2.PutText(image, "While we appreciate your enthusiasm, our team can only", new OpenCvSharp.Point(bodyLeft, bodyTop), HersheyFonts.HersheySimplex, 0.68, Scalar.All(220), 2, LineTypes.AntiAlias);
        Cv2.PutText(image, "process but so many submissions a day. Return in 23 hours,", new OpenCvSharp.Point(bodyLeft, bodyTop + 30), HersheyFonts.HersheySimplex, 0.68, Scalar.All(220), 2, LineTypes.AntiAlias);
        Cv2.PutText(image, "12 minutes and 4 seconds to continue contributing to the", new OpenCvSharp.Point(bodyLeft, bodyTop + 60), HersheyFonts.HersheySimplex, 0.68, Scalar.All(220), 2, LineTypes.AntiAlias);
        Cv2.PutText(image, "project.", new OpenCvSharp.Point(bodyLeft, bodyTop + 90), HersheyFonts.HersheySimplex, 0.68, Scalar.All(220), 2, LineTypes.AntiAlias);

        var button = new Rect(
            popup.X + (int)Math.Round(popup.Width * 0.04),
            popup.Y + (int)Math.Round(popup.Height * 0.80),
            (int)Math.Round(popup.Width * 0.90),
            (int)Math.Round(popup.Height * 0.12));
        Cv2.Rectangle(image, button, new Scalar(78, 63, 35), -1);
        Cv2.Rectangle(image, button, new Scalar(190, 170, 80), 1);
        Cv2.PutText(
            image,
            "OK",
            new OpenCvSharp.Point(button.X + (button.Width / 2) - 16, button.Y + (button.Height / 2) + 8),
            HersheyFonts.HersheySimplex,
            0.7,
            Scalar.All(220),
            1,
            LineTypes.AntiAlias);
    }

    private readonly record struct ClusterDefinition(OpenCvSharp.Point Center, OpenCvSharp.Size Size, Scalar Color);
}
