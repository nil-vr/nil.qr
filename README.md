This repository contains a nil.qr Unity package containing a QrCode component. The QrCode component uses U# code to render a QR codes to tiny textures. The included QrCode material uses a special shader so the textures stay sharp even when they are viewed at very large sizes.

# Installation

It's recommended that you install this package using [VCC].

1. Add my package repository my clicking this link: [https://nil-vr.github.io/vpm/install.html](https://nil-vr.github.io/vpm/install.html)
2. When managing your project in VCC, "nil.qr" will appear as an installable package.

[VCC]: https://vcc.docs.vrchat.com/

# Tips

## Colors

If you don't want your QR code to be black on white, you can copy the provided QrCode material (ctrl+drag from Packages to Assets) and then change the colors labeled Zero and One to whatever colors you want. Use your custom material in the Raw Image component of your QR code object.

# Troubleshooting

## The texture appears red and blurry

Make sure you are using the QrCode material. The texture is really red and blurry because it's a small, red-only texture to save resources. The shader used by the material magnifies the texture without blurring it and maps the red-only texture to RGBA.

## The QR code doesn't scan

Make sure the QR code is not flipped. The Raw Image component should have the UV Rect set to X: 0 Y: 1 W: 1 H: -1.

Do not remove or cover the white border around the QR code. This is part of the QR code, and scanners have trouble scanning QR codes that don't have it.
