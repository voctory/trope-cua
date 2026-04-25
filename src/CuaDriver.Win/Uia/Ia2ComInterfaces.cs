using System.Runtime.InteropServices;

namespace CuaDriver.Win.Uia;

[ComImport]
[Guid("B70D9F59-3B5A-4DBA-AB9E-22012F607DF5")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAccessibleAction
{
    [PreserveSig]
    int nActions(out int nActions);

    [PreserveSig]
    int doAction(int actionIndex);
}

[ComImport]
[Guid("A59AA09A-7011-4b65-939D-32B1FB5547E3")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAccessibleEditableText
{
    [PreserveSig]
    int copyText(int startOffset, int endOffset);

    [PreserveSig]
    int deleteText(int startOffset, int endOffset);

    [PreserveSig]
    int insertText(int offset, [MarshalAs(UnmanagedType.BStr)] ref string text);

    [PreserveSig]
    int cutText(int startOffset, int endOffset);

    [PreserveSig]
    int pasteText(int offset);

    [PreserveSig]
    int replaceText(int startOffset, int endOffset, [MarshalAs(UnmanagedType.BStr)] ref string text);

    [PreserveSig]
    int setAttributes(int startOffset, int endOffset, [MarshalAs(UnmanagedType.BStr)] ref string attributes);
}

[ComImport]
[Guid("24FD2FFB-3AAD-4a08-8335-A3AD89C0FB4B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAccessibleText
{
    [PreserveSig]
    int addSelection(int startOffset, int endOffset);

    [PreserveSig]
    int get_attributes(int offset, out int startOffset, out int endOffset, [MarshalAs(UnmanagedType.BStr)] out string textAttributes);

    [PreserveSig]
    int get_caretOffset(out int offset);

    [PreserveSig]
    int get_characterExtents(int offset, int coordType, out int x, out int y, out int width, out int height);

    [PreserveSig]
    int get_nSelections(out int nSelections);

    [PreserveSig]
    int get_offsetAtPoint(int x, int y, int coordType, out int offset);

    [PreserveSig]
    int get_selection(int selectionIndex, out int startOffset, out int endOffset);

    [PreserveSig]
    int get_text(int startOffset, int endOffset, [MarshalAs(UnmanagedType.BStr)] out string text);

    [PreserveSig]
    int get_textBeforeOffset(int offset, int boundaryType, out int startOffset, out int endOffset, [MarshalAs(UnmanagedType.BStr)] out string text);

    [PreserveSig]
    int get_textAfterOffset(int offset, int boundaryType, out int startOffset, out int endOffset, [MarshalAs(UnmanagedType.BStr)] out string text);

    [PreserveSig]
    int get_textAtOffset(int offset, int boundaryType, out int startOffset, out int endOffset, [MarshalAs(UnmanagedType.BStr)] out string text);

    [PreserveSig]
    int removeSelection(int selectionIndex);

    [PreserveSig]
    int setCaretOffset(int offset);

    [PreserveSig]
    int setSelection(int selectionIndex, int startOffset, int endOffset);

    [PreserveSig]
    int get_nCharacters(out int nCharacters);

    [PreserveSig]
    int scrollSubstringTo(int startIndex, int endIndex, int scrollType);

    [PreserveSig]
    int scrollSubstringToPoint(int startIndex, int endIndex, int coordinateType, int x, int y);
}

[ComImport]
[Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IServiceProvider
{
    [PreserveSig]
    int QueryService(ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
}
