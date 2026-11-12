using System.Windows;
using System.Windows.Controls;
using Merkautomat.Views;
using Xunit;

namespace Merkautomat.Tests;

public class BrainChoiceDialogTests
{
    private static BrainChoiceDialog.Choice Choice(string key, bool initial) => new() { Key = key, Title = "Eintrag " + key, Initial = initial };

    [Fact]
    public void Dialog_StartsWithInitialStateAndNeedsAName()
    {
        StaTestRunner.Run(() =>
        {
            TestApplication.Ensure();
            var dialog = new BrainChoiceDialog("Mappe bearbeiten", "Hinweis", "Outlook",
                [new BrainChoiceDialog.Section("IN DER MAPPE", [Choice("a", true), Choice("b", true)]),
                 new BrainChoiceDialog.Section("VORSCHLÄGE", [Choice("c", false)])],
                "Speichern", removeText: "Mappe auflösen");
            dialog.Show();
            dialog.UpdateLayout();

            Assert.Equal(["a", "b"], dialog.CheckedKeys);
            Assert.Equal("2 von 3 markiert", Assert.IsType<TextBlock>(dialog.FindName("CountText")).Text);
            Assert.Equal(Visibility.Visible, Assert.IsType<Button>(dialog.FindName("RemoveButton")).Visibility);
            var confirm = Assert.IsType<Button>(dialog.FindName("ConfirmButton"));
            Assert.True(confirm.IsEnabled);

            Assert.IsType<TextBox>(dialog.FindName("NameBox")).Text = "  ";
            Assert.False(confirm.IsEnabled);
            Assert.Equal("", dialog.NameText);
            dialog.Close();
        });
    }

    [Fact]
    public void Dialog_WithoutNameHidesNameFieldAndCanCheckAll()
    {
        StaTestRunner.Run(() =>
        {
            TestApplication.Ensure();
            var dialog = new BrainChoiceDialog("Offene Punkte als Aufgaben", "Hinweis", null,
                [new BrainChoiceDialog.Section("OFFENE PUNKTE", [Choice("0", false), Choice("1", false)])], "Aufgaben anlegen");

            Assert.Equal(Visibility.Collapsed, Assert.IsType<StackPanel>(dialog.FindName("NamePanel")).Visibility);
            Assert.Equal(Visibility.Collapsed, Assert.IsType<Button>(dialog.FindName("RemoveButton")).Visibility);
            Assert.True(Assert.IsType<Button>(dialog.FindName("ConfirmButton")).IsEnabled);
            Assert.Empty(dialog.CheckedKeys);
            foreach (var choice in dialog.Choices) choice.IsChecked = true;
            Assert.Equal(["0", "1"], dialog.CheckedKeys);
            dialog.Close();
        });
    }
}
