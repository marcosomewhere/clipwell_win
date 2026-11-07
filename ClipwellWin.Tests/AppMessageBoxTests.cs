using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using ClipwellWin.Views;
using Xunit;

namespace ClipwellWin.Tests;

public sealed class AppMessageBoxTests
{
    private static AppMessageBox Create(MessageBoxButton buttons, MessageBoxResult defaultResult)
        => (AppMessageBox)typeof(AppMessageBox).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)[0]
            .Invoke(["Text", "Titel", buttons, MessageBoxImage.Question, defaultResult]);

    private static MessageBoxResult Result(AppMessageBox box)
        => (MessageBoxResult)typeof(AppMessageBox).GetField("_result", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(box)!;

    [Fact]
    public void YesNoUsesGermanLabelsDefaultAndCancelMapping()
        => StaTestRunner.Run(() =>
        {
            var box = Create(MessageBoxButton.YesNo, MessageBoxResult.No);
            var buttons = ((StackPanel)box.FindName("ButtonPanel")).Children.OfType<System.Windows.Controls.Button>().ToList();
            Assert.Equal(["Ja", "Nein"], buttons.Select(b => (string)b.Content));
            Assert.True(buttons[1].IsDefault);
            Assert.True(buttons[1].IsCancel);
            Assert.Equal(MessageBoxResult.No, Result(box));
            Assert.Equal("Titel", box.Title);
            box.Close();
        });

    [Fact]
    public void OkCancelFallsBackToTheFirstButtonAsDefault()
        => StaTestRunner.Run(() =>
        {
            var box = Create(MessageBoxButton.OKCancel, MessageBoxResult.Yes);
            var buttons = ((StackPanel)box.FindName("ButtonPanel")).Children.OfType<System.Windows.Controls.Button>().ToList();
            Assert.Equal(["OK", "Abbrechen"], buttons.Select(b => (string)b.Content));
            Assert.True(buttons[0].IsDefault);
            Assert.Equal(MessageBoxResult.Cancel, Result(box));
            box.Close();
        });
}
