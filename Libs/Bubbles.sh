#!/bin/bash

OWNER="Jaxe-Dev"
REPO="Bubbles"
DLLNAME="Bubbles.dll"
SCRIPT_PATH="$( cd "$( dirname "$0" )" && pwd )"
API_BASE_URL="https://api.github.com/repos/$OWNER/$REPO"

README_CONTENT=$(curl -s "$API_BASE_URL/readme" | \
  jq -r '.content' | \
  base64 --decode)

printf "%s\n" "$README_CONTENT"

echo "==============================================="
echo "Interaction Bubbles by $OWNER."
echo "You can check project at 'https://github.com/$OWNER/$REPO'."
echo "==============================================="

LATEST_RELEASE_JSON=$(curl -s "$API_BASE_URL/releases/latest")
TAG_NAME=$(echo "$LATEST_RELEASE_JSON" | jq -r '.tag_name')

echo "Latest release version: $TAG_NAME"

DOWNLOAD_URL=$(echo "$LATEST_RELEASE_JSON" | jq -r '.assets[0].browser_download_url')
FILE_NAME=$(echo "$LATEST_RELEASE_JSON" | jq -r '.assets[0].name')

if [ "$DOWNLOAD_URL" == "null" ] || [ -z "$DOWNLOAD_URL" ]; then
  echo "Error: There's no github release asset!"
  exit 1
fi

OUTPUT_FILE="$SCRIPT_PATH/$FILE_NAME"

curl -L "$DOWNLOAD_URL" -o "$OUTPUT_FILE"

if [ $? -ne 0 ]; then
  echo "Error: Download failed!"
  exit 1
fi

echo "Fetch complete!"
echo "==============================================="
echo "Extracting..."

EXTRACT_PATH="$SCRIPT_PATH/Bubbles"

unzip -q -o "$OUTPUT_FILE" -d "$EXTRACT_PATH"

cp "$EXTRACT_PATH/Assemblies/$DLLNAME" "$SCRIPT_PATH/$DLLNAME"

rm "$OUTPUT_FILE"
rm -r "$EXTRACT_PATH"

echo "Done!"
