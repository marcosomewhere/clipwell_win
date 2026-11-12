using System.Windows.Automation;
using System.Windows.Automation.Peers;
using Merkautomat.Views;
using Xunit;

namespace Merkautomat.Tests;

public class AccessibilityTests
{
    [Fact]
    public void SevenSegmentDisplay_ReadsLabelAndValue()
    {
        StaTestRunner.Run(() =>
        {
            var display = new SevenSegmentDisplay { Value = 12 };
            AutomationProperties.SetName(display, "Sprachnotizen");

            var peer = UIElementAutomationPeer.CreatePeerForElement(display);

            Assert.Equal(AutomationControlType.Text, peer.GetAutomationControlType());
            Assert.Equal("Sprachnotizen: 12", peer.GetName());
            display.Value = 13;
            Assert.Equal("Sprachnotizen: 13", peer.GetName());
        });
    }

    [Fact]
    public void AnalogGauge_ReadsLabelAndReadingNotNeedle()
    {
        StaTestRunner.Run(() =>
        {
            var gauge = new AnalogGauge { Reading = 7 };
            AutomationProperties.SetName(gauge, "Aufnahme Fenster");

            Assert.Equal("Aufnahme Fenster: 7", UIElementAutomationPeer.CreatePeerForElement(gauge).GetName());
        });
    }

    [Fact]
    public void BrainCanvas_HasNameAndKeyboardHintWithoutGraph()
    {
        StaTestRunner.Run(() =>
        {
            var canvas = new BrainCanvas();
            var peer = UIElementAutomationPeer.CreatePeerForElement(canvas);

            Assert.Equal("Wissensnetz", peer.GetName());
            Assert.Equal("Wissensnetz", peer.GetLocalizedControlType());
            Assert.True(peer.IsKeyboardFocusable());
            Assert.Contains("Pfeiltasten", peer.GetHelpText());
            Assert.Null(canvas.SelectionDescription);
        });
    }
}
