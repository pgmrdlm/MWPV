# Prompt for base64 string (from keyset.json -> secrets.dbPassword)
$b64 = Read-Host "Enter base64 dbPassword"

try {
    # decode base64 → raw bytes
    $bytes = [Convert]::FromBase64String($b64)

    if ($bytes.Length -ne 32) {
        Write-Warning "Expected 32 bytes after base64 decode; got $($bytes.Length)"
    }

    # convert bytes → ASCII string
    $ascii = [System.Text.Encoding]::ASCII.GetString($bytes)

    # echo to console
    Write-Host "`nASCII passphrase (use this in DB Browser 'Password' box):" -ForegroundColor Cyan
    Write-Host $ascii

    # copy to clipboard
    Set-Clipboard $ascii
    Write-Host "`nAlso copied to clipboard." -ForegroundColor DarkGray
}
catch {
    Write-Error $_
    exit 1
}
