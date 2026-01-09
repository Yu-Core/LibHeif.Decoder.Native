#!/bin/sh
set -e

OUT="THIRD-PARTY-NOTICES.txt"

LIBHEIF_URL="https://raw.githubusercontent.com/strukturag/libheif/master/COPYING"
LIBDE265_URL="https://raw.githubusercontent.com/strukturag/libde265/master/COPYING"
DAV1D_URL="https://code.videolan.org/videolan/dav1d/-/raw/master/COPYING"

echo "Fetching libheif..."
LIBHEIF=$(curl -fsSL "$LIBHEIF_URL")

echo "Fetching libde265..."
LIBDE265=$(curl -fsSL "$LIBDE265_URL")

echo "Fetching dav1d..."
DAV1D=$(curl -fsSL "$DAV1D_URL")

# 直接拼文件，彻底绕开 sed
cat > "$OUT" <<EOF
LibHeif.Decoder.Native uses third-party libraries or other resources that
may be distributed under licenses different than this product.

The attached notices are provided for information only.

1. libheif (https://github.com/strukturag/libheif)
2. libde265 (https://github.com/strukturag/libde265)
3. dav1d (https://code.videolan.org/videolan/dav1d)

License notice for libheif
=========================================

$LIBHEIF

License notice for libde265
=========================================

$LIBDE265

License notice for dav1d
=========================================

$DAV1D
EOF

echo "Generated $OUT successfully"
