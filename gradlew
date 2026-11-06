#!/bin/sh
# Gradle wrapper script

APP_NAME="Gradle"
GRADLE_VERSION="8.4"
DIST_URL="https://services.gradle.org/distributions/gradle-${GRADLE_VERSION}-bin.zip"
DIST_FILE="gradle-${GRADLE_VERSION}-bin.zip"
GRADLE_USER_HOME="${GRADLE_USER_HOME:-$HOME/.gradle}"
WRAPPER_DIR="$GRADLE_USER_HOME/wrapper/dists/gradle-${GRADLE_VERSION}-bin"

die() { echo "$*" >&2; exit 1; }

# Download gradle distribution if needed
if [ ! -d "$WRAPPER_DIR" ]; then
    mkdir -p "$GRADLE_USER_HOME/wrapper/dists" 2>/dev/null || true
    mkdir -p "$WRAPPER_DIR"
    echo "Downloading Gradle ${GRADLE_VERSION}..."
    curl -sL "$DIST_URL" -o "$WRAPPER_DIR/$DIST_FILE" || die "Failed to download Gradle"
fi

# Find the actual gradle directory
GRADLE_DIR=$(find "$GRADLE_USER_HOME/wrapper/dists" -name "gradle-${GRADLE_VERSION}" -type d 2>/dev/null | head -1)
[ -z "$GRADLE_DIR" ] && die "Gradle not found"

# Unzip if needed
if [ ! -f "$GRADLE_DIR/bin/gradle" ]; then
    echo "Extracting Gradle..."
    unzip -q "$WRAPPER_DIR/$DIST_FILE" -d "$GRADLE_USER_HOME/wrapper/dists" 2>/dev/null || die "Failed to extract"
fi

exec "$GRADLE_DIR/bin/gradle" "$@"
