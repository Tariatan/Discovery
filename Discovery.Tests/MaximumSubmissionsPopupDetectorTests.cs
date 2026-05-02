using OpenCvSharp;

namespace Discovery.Tests;

public sealed class MaximumSubmissionsPopupDetectorTests
{
    [Fact]
    public void Detect_ImageContainsMaximumSubmissionsPopup_ReturnsTrue()
    {
        // Arrange
        using var image = SyntheticDiscoveryImageFactory.CreateMaximumSubmissionsPopupImage();
        var detector = new MaximumSubmissionsPopupDetector();

        // Act
        var detected = detector.Detect(image);

        // Assert
        Assert.True(detected);
    }

    [Fact]
    public void Detect_FullScreenImageContainsMaximumSubmissionsPopupOutsideLegacySearchArea_ReturnsTrue()
    {
        // Arrange
        using var image = SyntheticDiscoveryImageFactory.CreateWideScreenMaximumSubmissionsPopupImage();
        var detector = new MaximumSubmissionsPopupDetector();

        // Act
        var detected = detector.Detect(image);

        // Assert
        Assert.True(detected);
    }

    [Fact]
    public void Detect_FullScreenImageContainsCompactDimMaximumSubmissionsPopup_ReturnsTrue()
    {
        // Arrange
        using var image = CreateCompactDimMaximumSubmissionsPopupImage();
        var detector = new MaximumSubmissionsPopupDetector();

        // Act
        var detected = detector.Detect(image);

        // Assert
        Assert.True(detected);
    }

    [Fact]
    public void Detect_ImageDoesNotContainMaximumSubmissionsPopup_ReturnsFalse()
    {
        // Arrange
        using var image = SyntheticDiscoveryImageFactory.CreateTwoClusterImage();
        var detector = new MaximumSubmissionsPopupDetector();

        // Act
        var detected = detector.Detect(image);

        // Assert
        Assert.False(detected);
    }

    [Fact]
    public void Detect_FullScreenImageContainsBusyPilotSelectionUi_ReturnsFalse()
    {
        // Arrange
        using var image = CreateBusyPilotSelectionImage();
        var detector = new MaximumSubmissionsPopupDetector();

        // Act
        var detected = detector.Detect(image);

        // Assert
        Assert.False(detected);
    }

    [Fact]
    public void Detect_FullScreenImageContainsProjectDiscoveryInstructionPanel_ReturnsFalse()
    {
        // Arrange
        using var image = CreateProjectDiscoveryInstructionImage();
        var detector = new MaximumSubmissionsPopupDetector();

        // Act
        var detected = detector.Detect(image);

        // Assert
        Assert.False(detected);
    }

    [Fact]
    public void Detect_FullScreenImageContainsSubmissionResultUi_ReturnsFalse()
    {
        // Arrange
        using var image = CreateSubmissionResultImage();
        var detector = new MaximumSubmissionsPopupDetector();

        // Act
        var detected = detector.Detect(image);

        // Assert
        Assert.False(detected);
    }

    [Fact]
    public void Detect_FullScreenImageContainsBottomInventoryGrid_ReturnsFalse()
    {
        // Arrange
        using var image = CreateBottomInventoryGridImage();
        var detector = new MaximumSubmissionsPopupDetector();

        // Act
        var detected = detector.Detect(image);

        // Assert
        Assert.False(detected);
    }

    [Fact]
    public void Detect_FocusedCaptureInventoryCrop_ReturnsFalse()
    {
        // Arrange
        var imagePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "maximum-submissions-false-positive-inventory-crop.png");
        using var image = Cv2.ImRead(imagePath);
        Assert.False(image.Empty());
        var detector = new MaximumSubmissionsPopupDetector();

        // Act
        var detected = detector.Detect(image);

        // Assert
        Assert.False(detected);
    }

    private static Mat CreateBusyPilotSelectionImage()
    {
        var image = new Mat(new Size(3000, 1600), MatType.CV_8UC3, new Scalar(14, 18, 22));
        Cv2.PutText(image, "EVE", new Point(1320, 260), HersheyFonts.HersheySimplex, 4.0, Scalar.All(240), 9, LineTypes.AntiAlias);
        Cv2.PutText(image, "ONLINE", new Point(1360, 330), HersheyFonts.HersheySimplex, 1.0, Scalar.All(240), 2, LineTypes.AntiAlias);

        for (var index = 0; index < 3; index++)
        {
            var left = 760 + index * 500;
            DrawPilotCard(image, new Rect(left, 520, 430, 900), $"Pilot {index + 1}");
        }

        return image;
    }

    private static Mat CreateCompactDimMaximumSubmissionsPopupImage()
    {
        var image = new Mat(new Size(2551, 2008), MatType.CV_8UC3, new Scalar(18, 24, 26));
        var popup = new Rect(960, 825, 625, 386);
        Cv2.Rectangle(image, popup, new Scalar(7, 7, 7), -1);
        Cv2.Rectangle(image, popup, new Scalar(60, 55, 42), 1);

        var iconCenter = new Point(popup.X + 62, popup.Y + 58);
        Cv2.Circle(image, iconCenter, 24, new Scalar(150, 150, 150), -1, LineTypes.AntiAlias);
        Cv2.PutText(image, "i", new Point(iconCenter.X - 5, iconCenter.Y + 11), HersheyFonts.HersheyDuplex, 1.0, new Scalar(25, 25, 25), 2, LineTypes.AntiAlias);

        Cv2.PutText(image, "Maximum Number of", new Point(popup.X + 125, popup.Y + 55), HersheyFonts.HersheySimplex, 1.2, Scalar.All(235), 3, LineTypes.AntiAlias);
        Cv2.PutText(image, "Submissions Reached", new Point(popup.X + 125, popup.Y + 105), HersheyFonts.HersheySimplex, 1.2, Scalar.All(235), 3, LineTypes.AntiAlias);

        var bodyLeft = popup.X + 28;
        var bodyTop = popup.Y + 140;
        Cv2.PutText(image, "While we appreciate your enthusiasm, our team can only", new Point(bodyLeft, bodyTop), HersheyFonts.HersheySimplex, 0.62, Scalar.All(220), 2, LineTypes.AntiAlias);
        Cv2.PutText(image, "process but so many submissions a day. Return in 11 hours,", new Point(bodyLeft, bodyTop + 28), HersheyFonts.HersheySimplex, 0.62, Scalar.All(220), 2, LineTypes.AntiAlias);
        Cv2.PutText(image, "13 minutes and 1 second to continue contributing to the", new Point(bodyLeft, bodyTop + 56), HersheyFonts.HersheySimplex, 0.62, Scalar.All(220), 2, LineTypes.AntiAlias);
        Cv2.PutText(image, "project.", new Point(bodyLeft, bodyTop + 84), HersheyFonts.HersheySimplex, 0.62, Scalar.All(220), 2, LineTypes.AntiAlias);

        var button = new Rect(popup.X + 28, popup.Y + 310, popup.Width - 56, 50);
        Cv2.Rectangle(image, button, new Scalar(75, 60, 35), -1);
        Cv2.Rectangle(image, button, new Scalar(180, 165, 80), 1);
        Cv2.PutText(image, "OK", new Point(button.X + (button.Width / 2) - 15, button.Y + 32), HersheyFonts.HersheySimplex, 0.7, Scalar.All(220), 1, LineTypes.AntiAlias);
        return image;
    }

    private static void DrawPilotCard(Mat image, Rect bounds, string pilotName)
    {
        Cv2.Rectangle(image, bounds, new Scalar(12, 12, 12), -1);
        Cv2.Rectangle(image, new Rect(bounds.X, bounds.Y, bounds.Width, 42), new Scalar(35, 35, 35), -1);
        Cv2.PutText(image, pilotName, new Point(bounds.X + 18, bounds.Y + 28), HersheyFonts.HersheySimplex, 0.8, Scalar.All(220), 2, LineTypes.AntiAlias);
        Cv2.Rectangle(image, new Rect(bounds.X + 20, bounds.Y + 70, bounds.Width - 40, 380), Scalar.All(30), -1);
        Cv2.Circle(image, new Point(bounds.X + bounds.Width / 2, bounds.Y + 220), 90, Scalar.All(200), -1, LineTypes.AntiAlias);
        Cv2.Ellipse(image, new Point(bounds.X + bounds.Width / 2, bounds.Y + 390), new Size(110, 58), 0, 0, 360, Scalar.All(150), -1, LineTypes.AntiAlias);
        Cv2.PutText(image, "No skill in training", new Point(bounds.X + 30, bounds.Y + 500), HersheyFonts.HersheySimplex, 0.65, Scalar.All(225), 1, LineTypes.AntiAlias);
        Cv2.PutText(image, "157,850,771", new Point(bounds.X + 245, bounds.Y + 575), HersheyFonts.HersheySimplex, 0.65, Scalar.All(235), 1, LineTypes.AntiAlias);
        Cv2.PutText(image, "No unread mails", new Point(bounds.X + 245, bounds.Y + 645), HersheyFonts.HersheySimplex, 0.65, Scalar.All(235), 1, LineTypes.AntiAlias);
    }

    private static Mat CreateProjectDiscoveryInstructionImage()
    {
        var image = new Mat(new Size(2551, 2008), MatType.CV_8UC3, new Scalar(18, 22, 24));
        Cv2.Rectangle(image, new Rect(48, 40, 1900, 1080), new Scalar(5, 5, 5), -1);
        DrawPointCluster(image, new Point(570, 700));
        DrawInstructionPanel(image, new Rect(1350, 245, 485, 410));
        Cv2.Rectangle(image, new Rect(1350, 940, 485, 50), new Scalar(20, 20, 20), -1);
        Cv2.PutText(image, "Submit", new Point(1560, 974), HersheyFonts.HersheySimplex, 0.6, Scalar.All(75), 1, LineTypes.AntiAlias);
        return image;
    }

    private static void DrawPointCluster(Mat image, Point center)
    {
        var random = new Random(54321);
        for (var index = 0; index < 1_200; index++)
        {
            var offsetX = (int)Math.Round(((random.NextDouble() * 2.0) - 1.0) * 150.0);
            var offsetY = (int)Math.Round(((random.NextDouble() * 2.0) - 1.0) * 150.0);
            var color = index % 4 == 0 ? new Scalar(220, 170, 30) : new Scalar(190, 100, 45);
            Cv2.Circle(image, new Point(center.X + offsetX, center.Y + offsetY), 1, color, -1, LineTypes.AntiAlias);
        }
    }

    private static void DrawInstructionPanel(Mat image, Rect bounds)
    {
        Cv2.Rectangle(image, bounds, new Scalar(55, 45, 10), -1);
        Cv2.Rectangle(image, bounds, new Scalar(95, 85, 40), 1);
        Cv2.PutText(image, "Demarcate the clusters", new Point(bounds.X + 36, bounds.Y + 70), HersheyFonts.HersheySimplex, 1.0, Scalar.All(225), 2, LineTypes.AntiAlias);

        var bodyLines = new[]
        {
            "Define clusters by drawing polygons",
            "around them.",
            "Use changes in the curves to identify",
            "cluster boundaries.",
            "Take the time to recheck your work. You",
            "can always adjust your points.",
            "Each polygon can contain no more than",
            "10 points."
        };

        for (var index = 0; index < bodyLines.Length; index++)
        {
            var top = bounds.Y + 135 + index * 33;
            if (index % 2 == 0)
            {
                Cv2.Rectangle(image, new Rect(bounds.X + 35, top - 11, 12, 12), new Scalar(180, 135, 65), -1);
            }

            Cv2.PutText(image, bodyLines[index], new Point(bounds.X + 70, top), HersheyFonts.HersheySimplex, 0.54, Scalar.All(225), 1, LineTypes.AntiAlias);
        }
    }

    private static Mat CreateSubmissionResultImage()
    {
        var image = new Mat(new Size(1792, 1414), MatType.CV_8UC3, new Scalar(16, 20, 22));
        Cv2.Rectangle(image, new Rect(50, 35, 1320, 750), new Scalar(4, 4, 4), -1);
        DrawVerticalResultCluster(image, new Point(160, 220));
        DrawSubmissionResultPanel(image, new Rect(600, 180, 730, 467));
        return image;
    }

    private static void DrawVerticalResultCluster(Mat image, Point top)
    {
        var random = new Random(24680);
        for (var index = 0; index < 1_400; index++)
        {
            var x = top.X + random.Next(-16, 40);
            var y = top.Y + random.Next(0, 460);
            var color = index % 5 == 0
                ? new Scalar(0, 210, 255)
                : new Scalar(255, 120, 30);
            Cv2.Circle(image, new Point(x, y), 1, color, -1, LineTypes.AntiAlias);
        }
    }

    private static void DrawSubmissionResultPanel(Mat image, Rect candidate)
    {
        var iconLeft = candidate.X + 40;
        Cv2.Rectangle(image, new Rect(iconLeft, candidate.Y + 48, 52, 52), Scalar.All(230), -1);
        Cv2.PutText(image, "X", new Point(iconLeft + 13, candidate.Y + 85), HersheyFonts.HersheySimplex, 1.0, Scalar.All(20), 2, LineTypes.AntiAlias);
        Cv2.Rectangle(image, new Rect(iconLeft, candidate.Y + 126, 52, 52), Scalar.All(230), -1);
        Cv2.PutText(image, "Z", new Point(iconLeft + 13, candidate.Y + 164), HersheyFonts.HersheySimplex, 1.0, Scalar.All(20), 2, LineTypes.AntiAlias);

        Cv2.Rectangle(image, new Rect(candidate.X + 135, candidate.Y + 30, 300, 66), new Scalar(45, 35, 15), -1);
        Cv2.Rectangle(image, new Rect(candidate.X + 135, candidate.Y + 112, 300, 66), new Scalar(45, 35, 15), -1);
        Cv2.PutText(image, "Submission", new Point(candidate.X + 160, candidate.Y + 70), HersheyFonts.HersheySimplex, 1.0, Scalar.All(220), 2, LineTypes.AntiAlias);
        Cv2.PutText(image, "99,000", new Point(candidate.X + 160, candidate.Y + 154), HersheyFonts.HersheySimplex, 1.0, Scalar.All(220), 2, LineTypes.AntiAlias);

        Cv2.Rectangle(image, new Rect(candidate.X + 135, candidate.Y + 205, 450, 70), new Scalar(45, 35, 15), -1);
        Cv2.PutText(image, "Thank you for your submission", new Point(candidate.X + 160, candidate.Y + 250), HersheyFonts.HersheySimplex, 0.9, Scalar.All(220), 2, LineTypes.AntiAlias);

        Cv2.Rectangle(image, new Rect(candidate.X + 135, candidate.Y + 330, 450, 45), new Scalar(75, 60, 35), -1);
        Cv2.Rectangle(image, new Rect(candidate.X + 135, candidate.Y + 330, 450, 45), new Scalar(180, 165, 80), 1);
        Cv2.PutText(image, "Continue", new Point(candidate.X + 305, candidate.Y + 359), HersheyFonts.HersheySimplex, 0.6, Scalar.All(215), 1, LineTypes.AntiAlias);
    }

    private static Mat CreateBottomInventoryGridImage()
    {
        var image = new Mat(new Size(2551, 2008), MatType.CV_8UC3, new Scalar(15, 18, 20));
        Cv2.Rectangle(image, new Rect(48, 1325, 900, 650), new Scalar(5, 5, 5), -1);
        Cv2.Rectangle(image, new Rect(48, 1325, 900, 650), new Scalar(35, 45, 48), 1);
        Cv2.PutText(image, "Inventory", new Point(130, 1382), HersheyFonts.HersheySimplex, 0.9, Scalar.All(205), 2, LineTypes.AntiAlias);

        var start = new Point(330, 1510);
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                DrawInventoryItem(image, new Point(start.X + column * 135, start.Y + row * 170));
            }
        }

        Cv2.Rectangle(image, new Rect(297, 1725, 497, 85), new Scalar(45, 65, 68), -1);
        Cv2.Rectangle(image, new Rect(297, 1725, 497, 85), new Scalar(115, 160, 165), 1);
        Cv2.PutText(image, "Capital Ship", new Point(420, 1768), HersheyFonts.HersheySimplex, 0.7, Scalar.All(220), 1, LineTypes.AntiAlias);
        Cv2.PutText(image, "Capital Ship", new Point(620, 1795), HersheyFonts.HersheySimplex, 0.7, Scalar.All(220), 1, LineTypes.AntiAlias);
        return image;
    }

    private static void DrawInventoryItem(Mat image, Point topLeft)
    {
        var icon = new Rect(topLeft.X, topLeft.Y, 88, 57);
        Cv2.Rectangle(image, icon, new Scalar(55, 100, 110), 2);
        var ship = new[]
        {
            new Point(icon.X + 4, icon.Y + 28),
            new Point(icon.X + 50, icon.Y + 5),
            new Point(icon.X + 86, icon.Y + 25),
            new Point(icon.X + 44, icon.Y + 52)
        };
        Cv2.FillConvexPoly(image, ship, Scalar.All(210), LineTypes.AntiAlias);
        Cv2.Line(image, new Point(icon.X + 12, icon.Y + 44), new Point(icon.X + 83, icon.Y + 12), Scalar.All(235), 2, LineTypes.AntiAlias);
        Cv2.PutText(image, "Capital", new Point(topLeft.X + 4, topLeft.Y + 92), HersheyFonts.HersheySimplex, 0.58, Scalar.All(225), 1, LineTypes.AntiAlias);
        Cv2.PutText(image, "Ship", new Point(topLeft.X + 22, topLeft.Y + 126), HersheyFonts.HersheySimplex, 0.58, Scalar.All(225), 1, LineTypes.AntiAlias);
    }
}
