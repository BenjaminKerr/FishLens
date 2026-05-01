# ========================================
# Tesseract OCR Installation Script
# For Windows - FishLens Project
# ========================================

Write-Host "`n=== Tesseract OCR Setup ===" -ForegroundColor Cyan

# Check if Tesseract is already installed
$tesseractPath = "C:\Program Files\Tesseract-OCR\tesseract.exe"
if (Test-Path $tesseractPath) {
    Write-Host "Tesseract OCR is already installed at: $tesseractPath" -ForegroundColor Green
    & $tesseractPath --version
    exit 0
}

Write-Host "`nTesseract OCR not found. Installing..." -ForegroundColor Yellow

# Download Tesseract installer
$installerUrl = "https://digi.bib.uni-mannheim.de/tesseract/tesseract-ocr-w64-setup-5.3.3.20231005.exe"
$installerPath = "$env:TEMP\tesseract-installer.exe"

Write-Host "`nDownloading Tesseract OCR installer..."
try {
    Invoke-WebRequest -Uri $installerUrl -OutFile $installerPath -UseBasicParsing
    Write-Host "Download complete" -ForegroundColor Green
} catch {
    Write-Host "Download failed: $_" -ForegroundColor Red
    Write-Host "`nPlease manually download and install Tesseract from:" -ForegroundColor Yellow
    Write-Host "https://github.com/UB-Mannheim/tesseract/wiki" -ForegroundColor Cyan
    exit 1
}

# Run installer silently
Write-Host "`nInstalling Tesseract OCR..."
Write-Host "(This may take a minute...)"
try {
    $process = Start-Process -FilePath $installerPath -ArgumentList "/S" -Wait -PassThru
    
    if ($process.ExitCode -eq 0) {
        Write-Host "Tesseract OCR installed successfully!" -ForegroundColor Green
        
        # Verify installation
        if (Test-Path $tesseractPath) {
            Write-Host "`nVerified installation at: $tesseractPath" -ForegroundColor Green
            & $tesseractPath --version
        }
    } else {
        Write-Host "Installation failed with exit code: $($process.ExitCode)" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "Installation error: $_" -ForegroundColor Red
    exit 1
} finally {
    # Clean up installer
    if (Test-Path $installerPath) {
        Remove-Item $installerPath -Force
    }
}

Write-Host "`nSetup complete! OCR is ready to use." -ForegroundColor Green
