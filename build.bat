@echo off
chcp 65001 >nul
echo ==========================================
echo CaptureMouse Windows x64 构建脚本
echo ==========================================
echo.

REM 检查 dotnet
where dotnet >nul 2>nul
if %errorlevel% neq 0 (
    echo [错误] 未找到 dotnet，请先安装 .NET 9 SDK
    echo 下载地址: https://dotnet.microsoft.com/download
    exit /b 1
)

echo [1/5] 检查 .NET 版本...
dotnet --version

echo.
echo [2/5] 还原依赖...
dotnet restore src\CaptureMouse\CaptureMouse.csproj
if %errorlevel% neq 0 (
    echo [错误] 还原依赖失败
    exit /b 1
)

echo.
echo [3/5] 构建项目...
dotnet build src\CaptureMouse\CaptureMouse.csproj --configuration Release --no-restore
if %errorlevel% neq 0 (
    echo [错误] 构建失败
    exit /b 1
)

echo.
echo [4/5] 发布 x64 单文件...
dotnet publish src\CaptureMouse\CaptureMouse.csproj ^
    --configuration Release ^
    --runtime win-x64 ^
    --self-contained true ^
    --output .\publish\x64 ^
    --property:PublishSingleFile=true ^
    --property:PublishTrimmed=true ^
    --property:EnableCompressionInSingleFile=true ^
    --no-build

if %errorlevel% neq 0 (
    echo [错误] 发布失败
    exit /b 1
)

echo.
echo [5/5] 完成！
echo.
echo ==========================================
echo 输出文件:
dir .\publish\x64\*.exe /b
echo ==========================================
echo.
echo 使用方法:
echo   1. 在 macOS 上开启屏幕共享
echo   2. 运行 .\publish\x64\CaptureMouse.exe
echo   3. 输入 macOS IP 和密码
echo.
pause
