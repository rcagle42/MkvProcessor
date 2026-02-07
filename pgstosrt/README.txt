PgsToSrt Setup Instructions
============================

This folder should contain PgsToSrt for SUP to SRT subtitle conversion.

1. DOWNLOAD PGSTOSRT
--------------------
Download from: https://github.com/Tentacule/PgsToSrt/releases

Extract the following files to THIS folder (pgstosrt/):
- PgsToSrt.dll
- PgsToSrt.deps.json
- PgsToSrt.runtimeconfig.json
- Tesseract.dll (and other dependency DLLs)

2. DOWNLOAD TESSDATA (Language Files)
--------------------------------------
Download trained data files from: https://github.com/tesseract-ocr/tessdata

Place .traineddata files in the tessdata/ subfolder.

Common languages:
- eng.traineddata (English)
- spa.traineddata (Spanish)
- fra.traineddata (French)
- deu.traineddata (German)
- ita.traineddata (Italian)
- por.traineddata (Portuguese)
- jpn.traineddata (Japanese)
- kor.traineddata (Korean)
- chi_sim.traineddata (Chinese Simplified)
- chi_tra.traineddata (Chinese Traditional)

Direct download links (tessdata_fast - smaller, faster):
https://github.com/tesseract-ocr/tessdata_fast/raw/main/eng.traineddata
https://github.com/tesseract-ocr/tessdata_fast/raw/main/spa.traineddata
(etc.)

3. FOLDER STRUCTURE
-------------------
After setup, this folder should look like:

pgstosrt/
├── PgsToSrt.dll
├── PgsToSrt.deps.json
├── PgsToSrt.runtimeconfig.json
├── Tesseract.dll
├── (other DLLs...)
├── README.txt (this file)
└── tessdata/
    ├── eng.traineddata
    ├── spa.traineddata
    └── (other languages...)

4. VERIFICATION
---------------
The app will auto-detect PgsToSrt if placed here. The status indicator
in the Subtitle Converter tab will show green "Ready" when configured correctly.
