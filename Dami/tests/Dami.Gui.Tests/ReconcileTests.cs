using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Xunit;

namespace Dami.Gui.Tests;

public sealed class ReconcileTests
{
    private static (ObservableCollection<string> Target, List<NotifyCollectionChangedAction> Events) Watched(
        params string[] initial)
    {
        var target = new ObservableCollection<string>(initial);
        var events = new List<NotifyCollectionChangedAction>();
        target.CollectionChanged += (_, args) => events.Add(args.Action);
        return (target, events);
    }

    [Fact]
    public void Sync_Should_Not_Touch_The_Collection_When_Nothing_Changed()
    {
        // This is the whole point. The sidebars re-poll every 2 seconds and almost always
        // get back exactly what they already show; the old Clear()+re-add raised Reset,
        // which rebuilt every container and snapped the ScrollViewer back to the top.
        var (target, events) = Watched("a", "b", "c");

        var changed = Reconcile.Sync(target, ["a", "b", "c"]);

        Assert.False(changed);
        Assert.Empty(events);
    }

    [Fact]
    public void Sync_Should_Never_Raise_Reset()
    {
        // Reset is what destroys scroll position. Any real change must arrive as
        // Replace/Add/Remove so the list view can keep its viewport.
        var (target, events) = Watched("a", "b", "c");

        Reconcile.Sync(target, ["a", "x", "c", "d"]);

        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, events);
    }

    [Fact]
    public void Sync_Should_Replace_Only_The_Differing_Positions()
    {
        var (target, events) = Watched("a", "b", "c");

        var changed = Reconcile.Sync(target, ["a", "x", "c"]);

        Assert.True(changed);
        Assert.Equal(["a", "x", "c"], target);
        Assert.Equal([NotifyCollectionChangedAction.Replace], events);
    }

    [Fact]
    public void Sync_Should_Append_When_The_Desired_List_Is_Longer()
    {
        var (target, events) = Watched("a");

        Reconcile.Sync(target, ["a", "b", "c"]);

        Assert.Equal(["a", "b", "c"], target);
        Assert.Equal(
            [NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add], events);
    }

    [Fact]
    public void Sync_Should_Trim_From_The_Tail_When_The_Desired_List_Is_Shorter()
    {
        var (target, events) = Watched("a", "b", "c");

        Reconcile.Sync(target, ["a"]);

        Assert.Equal(["a"], target);
        Assert.Equal(
            [NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Remove], events);
    }

    [Fact]
    public void Sync_Should_Empty_A_Collection_Without_A_Reset()
    {
        var (target, events) = Watched("a", "b");

        var changed = Reconcile.Sync(target, []);

        Assert.True(changed);
        Assert.Empty(target);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, events);
    }

    [Fact]
    public void Sync_Should_Use_Value_Equality_So_Rebuilt_Items_Compare_Equal()
    {
        // Each poll constructs brand-new SidebarItem instances. Under reference equality
        // every poll would look like a full change, which is exactly the bug.
        var target = new ObservableCollection<SidebarItem>(
            [new SidebarItem("id1", "head", "detail")]);
        var events = new List<NotifyCollectionChangedAction>();
        target.CollectionChanged += (_, args) => events.Add(args.Action);

        var changed = Reconcile.Sync(target, [new SidebarItem("id1", "head", "detail")]);

        Assert.False(changed);
        Assert.Empty(events);
    }
}
