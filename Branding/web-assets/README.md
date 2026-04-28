# JabberJuicy Web Asset Pack

This folder contains web-ready assets extracted from the brand sheet at [source/jabberjuicy-asset-sheet.png](/Users/warrenrross/Education/DASC/DASC_Data_Management/GroupProjectEchoTeam/Branding/web-assets/source/jabberjuicy-asset-sheet.png).

## What's Included

- `logos/`: transparent logo lockups for header, hero, and footer use
- `icons/`: round marks, monograms, app icon variants, and small UI line icons
- `favicons/`: browser, Apple touch, and Android/PWA icon sizes
- `illustrations/`: transparent mascot and supporting decorative art
- `badges/`: achievement-style badge exports
- `ui/`: extracted button and interface reference elements
- `tokens/`: color, typography, and manifest files for web integration

## Recommended Usage

- Use `logos/logo-horizontal-transparent.png` for navbars and tighter spaces.
- Use `logos/logo-primary-transparent.png` for hero/header placements.
- Use `icons/icon-monogram-green.png` for very small marks and favicon-style uses.
- Use `icons/icon-app-purple.png` or `icons/icon-app-cream.png` for social/app-style square icons.
- Use the `icons/icon-*.png` line set at roughly `24px` to `32px` for best results.
- Use the design variables in `tokens/jabberjuicy.tokens.css` to match the palette shown on the sheet.

## Source Limitations

These files were extracted from a single raster asset sheet, not original layered/vector artwork.

- Transparent PNGs are cleanly isolated and ready for web use.
- Small icons are best used close to their exported size.
- `favicons/android-chrome-512x512.png` is an upscale from the sheet and is fine as a placeholder, but a higher-resolution master would be better for long-term app/PWA packaging.

## Quick Start

Link the stylesheet and favicon assets in your HTML:

```html
<link rel="stylesheet" href="./tokens/jabberjuicy.tokens.css" />
<link rel="icon" type="image/png" sizes="32x32" href="./favicons/favicon-32x32.png" />
<link rel="apple-touch-icon" href="./favicons/apple-touch-icon.png" />
<link rel="manifest" href="./tokens/site.webmanifest" />
```

Open [preview.html](/Users/warrenrross/Education/DASC/DASC_Data_Management/GroupProjectEchoTeam/Branding/web-assets/preview.html) in a browser to review the full package visually.
