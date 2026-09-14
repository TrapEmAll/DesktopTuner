#pragma once

#include <cstddef>
#include <string>
#include <string_view>

inline std::wstring QuoteWindowsCommandLineArgument(std::wstring_view argument)
{
    std::wstring quoted;
    quoted.push_back(L'"');

    std::size_t backslashCount = 0;
    for (const wchar_t character : argument)
    {
        if (character == L'\\')
        {
            ++backslashCount;
            continue;
        }

        if (character == L'"')
        {
            quoted.append(backslashCount * 2 + 1, L'\\');
            quoted.push_back(character);
        }
        else
        {
            quoted.append(backslashCount, L'\\');
            quoted.push_back(character);
        }
        backslashCount = 0;
    }

    quoted.append(backslashCount * 2, L'\\');
    quoted.push_back(L'"');
    return quoted;
}
