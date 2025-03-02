﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using static Interop;

namespace System.Windows.Forms.PropertyGridInternal
{
    internal partial class PropertyGridView
    {
        private sealed partial class GridViewTextBox : TextBox, IMouseHookClient
        {
            private bool _filter;
            private int _lastMove;

            private readonly MouseHook _mouseHook;

            public GridViewTextBox(PropertyGridView gridView)
            {
                PropertyGridView = gridView;
                _mouseHook = new MouseHook(this, this, gridView);
            }

            internal PropertyGridView PropertyGridView { get; }

            public bool InSetText { get; private set; }

            /// <summary>
            ///  Setting this to true will cause this <see cref="GridViewTextBox"/> to always
            ///  report that it is not focused.
            /// </summary>
            public bool HideFocusState { private get; set; }

            public bool Filter
            {
                get => _filter;
                set => _filter = value;
            }

            /// <inheritdoc />
            internal override bool SupportsUiaProviders => true;

            public override bool Focused => !HideFocusState && base.Focused;

            public override string Text
            {
                get => base.Text;
                set
                {
                    InSetText = true;
                    base.Text = value;
                    InSetText = false;
                }
            }

            public bool DisableMouseHook
            {
                set => _mouseHook.DisableMouseHook = value;
            }

            public bool HookMouseDown
            {
                get => _mouseHook.HookMouseDown;
                set
                {
                    _mouseHook.HookMouseDown = value;
                    if (value)
                    {
                        Focus();
                    }
                }
            }

            /// <inheritdoc />
            protected override AccessibleObject CreateAccessibilityInstance() => new GridViewTextBoxAccessibleObject(this);

            protected override void DestroyHandle()
            {
                _mouseHook.HookMouseDown = false;
                base.DestroyHandle();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _mouseHook.Dispose();
                }

                base.Dispose(disposing);
            }

            public void FilterKeyPress(char keyChar)
            {
                if (IsInputChar(keyChar))
                {
                    Focus();
                    SelectAll();
                    User32.PostMessageW(this, User32.WM.CHAR, (IntPtr)keyChar);
                }
            }

            /// <summary>
            ///  Overridden to handle TAB key.
            /// </summary>
            protected override bool IsInputKey(Keys keyData)
            {
                switch (keyData & Keys.KeyCode)
                {
                    case Keys.Escape:
                    case Keys.Tab:
                    case Keys.F4:
                    case Keys.F1:
                    case Keys.Return:
                        return false;
                }

                if (PropertyGridView.EditTextBoxNeedsCommit)
                {
                    return false;
                }

                return base.IsInputKey(keyData);
            }

            protected override bool IsInputChar(char keyChar) => (Keys)keyChar switch
            {
                // Overridden to handle TAB key.
                Keys.Tab or Keys.Return => false,
                _ => base.IsInputChar(keyChar),
            };

            protected override void OnGotFocus(EventArgs e)
            {
                base.OnGotFocus(e);

                AccessibilityObject.RaiseAutomationEvent(UiaCore.UIA.AutomationFocusChangedEventId);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                // This is because on a dialog we may not get a chance to pre-process.
                if (ProcessDialogKey(e.KeyData))
                {
                    e.Handled = true;
                    return;
                }

                base.OnKeyDown(e);
            }

            protected override void OnKeyPress(KeyPressEventArgs e)
            {
                if (!IsInputChar(e.KeyChar))
                {
                    e.Handled = true;
                    return;
                }

                base.OnKeyPress(e);
            }

            public bool OnClickHooked() => !PropertyGridView.CommitEditTextBox();

            protected override void OnMouseEnter(EventArgs e)
            {
                base.OnMouseEnter(e);

                if (!Focused)
                {
                    using Graphics graphics = CreateGraphics();
                    if (PropertyGridView.SelectedGridEntry is not null &&
                        ClientRectangle.Width <= PropertyGridView.SelectedGridEntry.GetValueTextWidth(Text, graphics, Font))
                    {
                        PropertyGridView.ToolTip.ToolTip = PasswordProtect ? "" : Text;
                    }
                }
            }

            protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
            {
                // Make sure we allow the Edit to handle ctrl-z
                switch (keyData & Keys.KeyCode)
                {
                    case Keys.Z:
                    case Keys.C:
                    case Keys.X:
                    case Keys.V:
                        if (((keyData & Keys.Control) != 0) && ((keyData & Keys.Shift) == 0) && ((keyData & Keys.Alt) == 0))
                        {
                            return false;
                        }

                        break;

                    case Keys.A:
                        if (((keyData & Keys.Control) != 0) && ((keyData & Keys.Shift) == 0) && ((keyData & Keys.Alt) == 0))
                        {
                            SelectAll();
                            return true;
                        }

                        break;

                    case Keys.Insert:
                        if (((keyData & Keys.Alt) == 0))
                        {
                            if (((keyData & Keys.Control) != 0) ^ ((keyData & Keys.Shift) == 0))
                            {
                                return false;
                            }
                        }

                        break;

                    case Keys.Delete:
                        if (((keyData & Keys.Control) == 0) && ((keyData & Keys.Shift) != 0) && ((keyData & Keys.Alt) == 0))
                        {
                            return false;
                        }
                        else if (((keyData & Keys.Control) == 0) && ((keyData & Keys.Shift) == 0) && ((keyData & Keys.Alt) == 0))
                        {
                            // If this is just the delete key and we're on a non-text editable property that
                            // is resettable, reset it now.
                            if (PropertyGridView.SelectedGridEntry is not null
                                && !PropertyGridView.SelectedGridEntry.Enumerable
                                && !PropertyGridView.SelectedGridEntry.IsTextEditable
                                && PropertyGridView.SelectedGridEntry.CanResetPropertyValue())
                            {
                                object oldValue = PropertyGridView.SelectedGridEntry.PropertyValue;
                                PropertyGridView.SelectedGridEntry.ResetPropertyValue();
                                PropertyGridView.UnfocusSelection();
                                PropertyGridView.OwnerGrid.OnPropertyValueSet(PropertyGridView.SelectedGridEntry, oldValue);
                            }
                        }

                        break;
                }

                return base.ProcessCmdKey(ref msg, keyData);
            }

            /// <inheritdoc />
            protected override bool ProcessDialogKey(Keys keyData)
            {
                // We don't do anything with modified keys here.
                if ((keyData & (Keys.Shift | Keys.Control | Keys.Alt)) == 0)
                {
                    switch (keyData & Keys.KeyCode)
                    {
                        case Keys.Return:
                            bool fwdReturn = !PropertyGridView.EditTextBoxNeedsCommit;
                            if (PropertyGridView.UnfocusSelection() && fwdReturn && PropertyGridView.SelectedGridEntry is not null)
                            {
                                PropertyGridView.SelectedGridEntry.OnValueReturnKey();
                            }

                            return true;
                        case Keys.Escape:
                            PropertyGridView.OnEscape(this);
                            return true;
                        case Keys.F4:
                            PropertyGridView.F4Selection(true);
                            return true;
                    }
                }

                // For the tab key we want to commit before we allow it to be processed.
                if ((keyData & Keys.KeyCode) == Keys.Tab && ((keyData & (Keys.Control | Keys.Alt)) == 0))
                {
                    return !PropertyGridView.CommitEditTextBox();
                }

                return base.ProcessDialogKey(keyData);
            }

            protected override void SetVisibleCore(bool value)
            {
                Debug.WriteLineIf(CompModSwitches.DebugGridView.TraceVerbose, $"DropDownHolder:Visible({value})");

                // Make sure we don't have the mouse captured if we're going invisible.
                if (value == false && HookMouseDown)
                {
                    _mouseHook.HookMouseDown = false;
                }

                base.SetVisibleCore(value);
            }

            private unsafe bool WmNotify(ref Message m)
            {
                if (m.LParam != IntPtr.Zero)
                {
                    User32.NMHDR* nmhdr = (User32.NMHDR*)m.LParam;

                    if (nmhdr->hwndFrom == PropertyGridView.ToolTip.Handle)
                    {
                        switch ((ComCtl32.TTN)nmhdr->code)
                        {
                            case ComCtl32.TTN.SHOW:
                                PositionTooltip(this, PropertyGridView.ToolTip, ClientRectangle);
                                m.Result = (IntPtr)1;
                                return true;
                            default:
                                PropertyGridView.WndProc(ref m);
                                break;
                        }
                    }
                }

                return false;
            }

            protected override void WndProc(ref Message m)
            {
                if (_filter && PropertyGridView.FilterEditWndProc(ref m))
                {
                    return;
                }

                switch ((User32.WM)m.Msg)
                {
                    case User32.WM.STYLECHANGED:
                        if ((unchecked((int)(long)m.WParam) & (int)User32.GWL.EXSTYLE) != 0)
                        {
                            PropertyGridView.Invalidate();
                        }

                        break;
                    case User32.WM.MOUSEMOVE:
                        if (unchecked((int)(long)m.LParam) == _lastMove)
                        {
                            return;
                        }

                        _lastMove = unchecked((int)(long)m.LParam);
                        break;
                    case User32.WM.DESTROY:
                        _mouseHook.HookMouseDown = false;
                        break;
                    case User32.WM.SHOWWINDOW:
                        if (IntPtr.Zero == m.WParam)
                        {
                            _mouseHook.HookMouseDown = false;
                        }

                        break;
                    case User32.WM.PASTE:
                        if (ReadOnly)
                        {
                            return;
                        }

                        break;

                    case User32.WM.GETDLGCODE:

                        m.Result = (IntPtr)((long)m.Result | (int)User32.DLGC.WANTARROWS | (int)User32.DLGC.WANTCHARS);
                        if (PropertyGridView.EditTextBoxNeedsCommit || PropertyGridView.WantsTab(forward: (ModifierKeys & Keys.Shift) == 0))
                        {
                            m.Result = (IntPtr)((long)m.Result | (int)User32.DLGC.WANTALLKEYS | (int)User32.DLGC.WANTTAB);
                        }

                        return;

                    case User32.WM.NOTIFY:
                        if (WmNotify(ref m))
                        {
                            return;
                        }

                        break;
                }

                base.WndProc(ref m);
            }
        }
    }
}
