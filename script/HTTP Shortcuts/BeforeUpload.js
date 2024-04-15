let text = getClipboardContent()
if (text != null) {
    showToast(text)
    setVariable('Clipboard', text)
} else {
    showToast('null clipboard value')
}