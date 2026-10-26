@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "APP_DIR=%~dp0"
set "REQ_FILE=%APP_DIR%requirements.txt"

if not exist "%REQ_FILE%" (
    echo requirements.txt not found at "%REQ_FILE%".
    exit /b 0
)

set "PYTHON_EXE="
if exist "%APP_DIR%python\python.exe" (
    set "PYTHON_EXE=%APP_DIR%python\python.exe"
) else if exist "%APP_DIR%python.exe" (
    set "PYTHON_EXE=%APP_DIR%python.exe"
) else if exist "%APP_DIR%Scripts\python.exe" (
    set "PYTHON_EXE=%APP_DIR%Scripts\python.exe"
) else (
    where python.exe >nul 2^>^&1
    if not errorlevel 1 for /f "delims=" %%I in ('where python.exe') do (
        set "PYTHON_EXE=%%~fI"
        goto :found_python
    )
)

:found_python
if not defined PYTHON_EXE (
    echo Could not find a Python interpreter to install dependencies.
    exit /b 0
)

"%PYTHON_EXE%" -m pip install --disable-pip-version-check -r "%REQ_FILE%"
exit /b %ERRORLEVEL%
