#include <windows.h>
#include <shellapi.h>
#include <iostream>
#include <string>
#include <vector>
#include "../WindowsCommandLine.h"

int wmain()
{
    const std::vector<std::wstring> arguments =
    {
        L"C:\\Users\\Sample User\\DesktopTuner.exe",
        L"--open-folder",
        L"C:\\",
        L"C:\\Users\\Sample User\\",
        L"C:\\Users\\Sample User\\Documents",
        L"--open-shell-location",
        L"::{20D04FE0-3AEA-1069-A2D8-08002B30309D}",
        L"path with \\\"embedded quotes\\\" and spaces",
        L""
    };

    std::wstring commandLine;
    for (const auto& argument : arguments)
    {
        if (!commandLine.empty()) commandLine.push_back(L' ');
        commandLine += QuoteWindowsCommandLineArgument(argument);
    }

    int actualCount = 0;
    LPWSTR* actualArguments = CommandLineToArgvW(commandLine.c_str(), &actualCount);
    if (actualArguments == nullptr)
    {
        std::wcerr << L"CommandLineToArgvW failed with error " << GetLastError() << L"\n";
        return 1;
    }

    bool matches = actualCount == static_cast<int>(arguments.size());
    for (int index = 0; matches && index < actualCount; ++index)
        matches = arguments[static_cast<size_t>(index)] == actualArguments[index];
    LocalFree(actualArguments);

    if (!matches)
    {
        std::wcerr << L"Windows argument quoting did not round-trip paths and arguments.\n";
        return 1;
    }

    std::wcout << L"Passed Windows command-line quoting round-trip checks.\n";
    return 0;
}
