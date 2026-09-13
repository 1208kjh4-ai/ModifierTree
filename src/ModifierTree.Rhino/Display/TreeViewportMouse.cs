using Rhino;
using Rhino.Commands;
using Rhino.UI;
using Rhino.UI.Gumball;

namespace ModifierTree.Rhino.Display;

internal sealed class TreeViewportMouse(DocumentSession session) : MouseCallback, IDisposable
{
    private bool _handledDown;
    private bool CanHandle(MouseCallbackEventArgs e) => !e.Cancel && session.PreviewEnabled && !session.IsStateRefreshPending &&
        e.View.Document.RuntimeSerialNumber == session.Document.RuntimeSerialNumber &&
        e.Button == System.Windows.Forms.MouseButtons.Left && !e.ShiftKeyDown && !e.CtrlKeyDown &&
        !Command.InCommand() && !session.Document.UndoActive && !session.Document.RedoActive && e.IsOverGumball() == GumballMode.None;

    protected override void OnMouseDown(MouseCallbackEventArgs e)
    {
        _handledDown = false;
        if (!CanHandle(e)) return;
        var picked = TreeViewportPicker.Pick(session, e.View, e.ViewportPoint);
        if (picked is not { } id)
        {
            if (TreeViewportPicker.OnlyHiddenInputHit(session, e.View, e.ViewportPoint))
            {
                e.Cancel = _handledDown = true;
                session.ClearViewportSelection();
            }
            return;
        }
        // Rhino can briefly report no Gumball hit while a native handle is being pressed.
        // Preserve the current valid selection so Rhino can start its own drag instead of
        // having this callback cancel the click and recreate the Gumball.
        if (session.PreserveCurrentGumballMouseDown(id)) return;
        e.Cancel = _handledDown = true;
        session.SelectInViewport(id);
    }

    protected override void OnMouseUp(MouseCallbackEventArgs e)
    {
        if (_handledDown && e.View.Document.RuntimeSerialNumber == session.Document.RuntimeSerialNumber &&
            e.Button == System.Windows.Forms.MouseButtons.Left) e.Cancel = true;
        _handledDown = false;
    }

    protected override void OnMouseDoubleClick(MouseCallbackEventArgs e)
    {
        if (!CanHandle(e)) return;
        var picked = TreeViewportPicker.Pick(session, e.View, e.ViewportPoint);
        if (picked is { } id)
        {
            e.Cancel = _handledDown = true;
            if (session.EditScope.Enter(id)) session.EnteredScope();
            else session.SelectInViewport(id);
        }
        else if (session.EditScope.ParentId.HasValue && !TreeViewportPicker.HasVisibleHit(session, e.View, e.ViewportPoint))
        {
            e.Cancel = _handledDown = true;
            session.ExitScope();
        }
    }

    public void Dispose() => Enabled = false;
}
