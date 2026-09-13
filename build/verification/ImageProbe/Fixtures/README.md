# Image probe fixture

`blue-64x48.heic` is a generated 64 × 48 opaque blue image (RGB 0, 0, 255), used under both `.heic` and `.heif` filenames. The probe requires matching dimensions and blue pixels within a five-level per-channel lossy codec tolerance.

The fixture was created for this repository from a generated RGB PNG using the macOS command-line encoder:

```sh
sips -s format heic blue-64x48.png --out blue-64x48.heic
```

It is checked in because the Magick.NET package supports HEIC decoding without providing a HEIC encoder. CI reads the fixture with the actual product conversion path; it does not invoke `sips`, open a UI, or skip HEIC verification when an encoder is unavailable.
