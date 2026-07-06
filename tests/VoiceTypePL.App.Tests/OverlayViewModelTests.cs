using System.ComponentModel;
using VoiceTypePL.App.Overlay;

namespace VoiceTypePL.App.Tests;

/// <summary>Testy modelu widoku dymka: powiadomienia i etykieta „+N oczekujące".</summary>
public sealed class OverlayViewModelTests
{
    [Fact]
    public void PendingCount_Zero_HasNoPendingText()
    {
        var vm = new OverlayViewModel { PendingCount = 0 };
        Assert.False(vm.HasPending);
        Assert.Equal(string.Empty, vm.PendingText);
    }

    [Fact]
    public void PendingCount_Positive_FormatsLabel()
    {
        var vm = new OverlayViewModel { PendingCount = 2 };
        Assert.True(vm.HasPending);
        Assert.Equal("+2 oczekujące", vm.PendingText);
    }

    [Fact]
    public void PendingCount_Change_RaisesDependentProperties()
    {
        var vm = new OverlayViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.PendingCount = 1;

        Assert.Contains(nameof(OverlayViewModel.PendingCount), raised);
        Assert.Contains(nameof(OverlayViewModel.HasPending), raised);
        Assert.Contains(nameof(OverlayViewModel.PendingText), raised);
    }

    [Fact]
    public void Text_Change_RaisesPropertyChanged()
    {
        var vm = new OverlayViewModel();
        string? changed = null;
        vm.PropertyChanged += (_, e) => changed = e.PropertyName;

        vm.Text = "nowy tekst";

        Assert.Equal(nameof(OverlayViewModel.Text), changed);
        Assert.Equal("nowy tekst", vm.Text);
    }

    [Fact]
    public void OldText_Empty_HidesPreview()
    {
        var vm = new OverlayViewModel();
        Assert.False(vm.HasOldText);
    }

    [Fact]
    public void OldText_Set_ShowsPreview_AndRaisesDependent()
    {
        var vm = new OverlayViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.OldText = "stare zdanie.";

        Assert.True(vm.HasOldText);
        Assert.Contains(nameof(OverlayViewModel.OldText), raised);
        Assert.Contains(nameof(OverlayViewModel.HasOldText), raised);
    }

    [Fact]
    public void OldText_Cleared_HidesPreviewAgain()
    {
        var vm = new OverlayViewModel { OldText = "stare" };
        vm.OldText = string.Empty;
        Assert.False(vm.HasOldText);
    }
}
