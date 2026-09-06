#!/usr/bin/env bash
# 一键构建并同步到桌面
set -e
cd "$(dirname "$0")"
dotnet publish -c Release
cp -f bin/Release/net8.0-windows/win-x64/publish/Oblivionis.exe "/d/桌面/Oblivionis.exe"
echo "已同步到桌面: /d/桌面/Oblivionis.exe"
