using Fuse.Core.Authoring;
using Xunit;

namespace Fuse.Core.Tests;

public class UndoServiceTests
{
    [Fact]
    public void Execute_Undo_Redo_Roundtrips_State()
    {
        var value = 0;
        var undo = new UndoService();

        undo.Execute(new UndoAction("+5", () => value += 5, () => value -= 5));
        undo.Execute(new UndoAction("*2", () => value *= 2, () => value /= 2));
        Assert.Equal(10, value); // (0+5)*2
        Assert.True(undo.CanUndo);
        Assert.False(undo.CanRedo);
        Assert.Equal("*2", undo.NextUndoLabel);

        undo.Undo();
        Assert.Equal(5, value);
        undo.Undo();
        Assert.Equal(0, value);
        Assert.False(undo.CanUndo);
        Assert.True(undo.CanRedo);

        undo.Redo();
        Assert.Equal(5, value);
        undo.Redo();
        Assert.Equal(10, value);
    }

    [Fact]
    public void New_Action_Clears_Redo()
    {
        var value = 0;
        var undo = new UndoService();
        undo.Execute(new UndoAction("+1", () => value += 1, () => value -= 1));
        undo.Undo();
        Assert.True(undo.CanRedo);

        undo.Execute(new UndoAction("+10", () => value += 10, () => value -= 10));

        Assert.False(undo.CanRedo);
        Assert.Equal(10, value);
    }
}
