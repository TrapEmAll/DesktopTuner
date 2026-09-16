#include <windows.h>
#include <shobjidl.h>
#include <shlwapi.h>
#include <string>
#include <new>
#include "WindowsCommandLine.h"

namespace
{
    constexpr CLSID CLSID_DesktopTunerExplorerCommand =
    { 0x8f2f0c1a, 0x9e63, 0x4d10, { 0xa2, 0xe5, 0x6f, 0x1c, 0xc9, 0x75, 0x64, 0x38 } };

    volatile long g_objectCount = 0;
    volatile long g_serverLockCount = 0;

    HRESULT CopyString(const wchar_t* value, PWSTR* result)
    {
        if (result == nullptr) return E_POINTER;
        *result = nullptr;
        const size_t length = wcslen(value) + 1;
        auto buffer = static_cast<PWSTR>(CoTaskMemAlloc(length * sizeof(wchar_t)));
        if (buffer == nullptr) return E_OUTOFMEMORY;
        wcscpy_s(buffer, length, value);
        *result = buffer;
        return S_OK;
    }

    bool GetInstallDirectory(std::wstring& directory)
    {
        HMODULE module = nullptr;
        const auto address = reinterpret_cast<LPCWSTR>(reinterpret_cast<ULONG_PTR>(&DllGetClassObject));
        if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT, address, &module))
            return false;

        wchar_t path[MAX_PATH]{};
        const DWORD length = GetModuleFileNameW(module, path, ARRAYSIZE(path));
        if (length == 0 || length >= ARRAYSIZE(path)) return false;
        directory.assign(path, length);
        const auto separator = directory.find_last_of(L"\\/");
        if (separator == std::wstring::npos) return false;
        directory.resize(separator);
        return true;
    }

    bool GetItemPath(IShellItemArray* items, std::wstring& itemPath)
    {
        if (items == nullptr) return false;
        DWORD count = 0;
        if (FAILED(items->GetCount(&count))) return false;

        for (DWORD index = 0; index < count; ++index)
        {
            IShellItem* item = nullptr;
            if (FAILED(items->GetItemAt(index, &item)) || item == nullptr) continue;

            PWSTR displayName = nullptr;
            const HRESULT displayResult = item->GetDisplayName(SIGDN_FILESYSPATH, &displayName);
            item->Release();
            if (FAILED(displayResult) || displayName == nullptr) continue;

            std::wstring candidate(displayName);
            CoTaskMemFree(displayName);
            if (GetFileAttributesW(candidate.c_str()) == INVALID_FILE_ATTRIBUTES) continue;
            itemPath = std::move(candidate);
            return true;
        }
        return false;
    }

    HRESULT OpenFolderInDesktopTuner(IShellItemArray* items)
    {
        std::wstring itemPath;
        if (!GetItemPath(items, itemPath)) return HRESULT_FROM_WIN32(ERROR_PATH_NOT_FOUND);
        const DWORD attributes = GetFileAttributesW(itemPath.c_str());
        if (attributes == INVALID_FILE_ATTRIBUTES) return HRESULT_FROM_WIN32(ERROR_PATH_NOT_FOUND);
        const bool isFile = (attributes & FILE_ATTRIBUTE_DIRECTORY) == 0;

        std::wstring installDirectory;
        if (!GetInstallDirectory(installDirectory)) return HRESULT_FROM_WIN32(GetLastError());
        const std::wstring executable = installDirectory + L"\\DesktopTuner.exe";
        const std::wstring argument = isFile ? L"--open-file-location" : L"--open-folder";
        const std::wstring command = QuoteWindowsCommandLineArgument(executable) + L" " + argument + L" " + QuoteWindowsCommandLineArgument(itemPath);
        std::wstring mutableCommand = command;

        STARTUPINFOW startup{};
        startup.cb = sizeof(startup);
        PROCESS_INFORMATION process{};
        if (!CreateProcessW(executable.c_str(), mutableCommand.data(), nullptr, nullptr, FALSE,
            CREATE_UNICODE_ENVIRONMENT, nullptr, installDirectory.c_str(), &startup, &process))
            return HRESULT_FROM_WIN32(GetLastError());

        CloseHandle(process.hThread);
        CloseHandle(process.hProcess);
        return S_OK;
    }

    bool HasFileSystemItem(IShellItemArray* items)
    {
        std::wstring path;
        return GetItemPath(items, path);
    }

    class ExplorerCommand final : public IExplorerCommand
    {
    public:
        ExplorerCommand() { InterlockedIncrement(&g_objectCount); }
        ~ExplorerCommand() { InterlockedDecrement(&g_objectCount); }

        HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** object) override
        {
            if (object == nullptr) return E_POINTER;
            *object = nullptr;
            if (IsEqualIID(iid, IID_IUnknown) || IsEqualIID(iid, IID_IExplorerCommand))
            {
                *object = static_cast<IExplorerCommand*>(this);
                AddRef();
                return S_OK;
            }
            return E_NOINTERFACE;
        }

        ULONG STDMETHODCALLTYPE AddRef() override { return static_cast<ULONG>(InterlockedIncrement(&references_)); }
        ULONG STDMETHODCALLTYPE Release() override
        {
            const ULONG remaining = static_cast<ULONG>(InterlockedDecrement(&references_));
            if (remaining == 0) delete this;
            return remaining;
        }

        HRESULT STDMETHODCALLTYPE GetTitle(IShellItemArray*, PWSTR* title) override
        {
            return CopyString(L"Open with Desktop Tuner", title);
        }

        HRESULT STDMETHODCALLTYPE GetIcon(IShellItemArray*, PWSTR* icon) override
        {
            if (icon == nullptr) return E_POINTER;
            std::wstring directory;
            if (!GetInstallDirectory(directory)) return HRESULT_FROM_WIN32(ERROR_PATH_NOT_FOUND);
            return CopyString((directory + L"\\DesktopTuner.exe,0").c_str(), icon);
        }

        HRESULT STDMETHODCALLTYPE GetToolTip(IShellItemArray*, PWSTR* tooltip) override
        {
            if (tooltip == nullptr) return E_POINTER;
            return CopyString(L"Open the selected item location in Desktop Tuner Explorer", tooltip);
        }

        HRESULT STDMETHODCALLTYPE GetCanonicalName(GUID* name) override
        {
            if (name == nullptr) return E_POINTER;
            *name = CLSID_DesktopTunerExplorerCommand;
            return S_OK;
        }

        HRESULT STDMETHODCALLTYPE GetState(IShellItemArray* items, BOOL, EXPCMDSTATE* state) override
        {
            if (state == nullptr) return E_POINTER;
            *state = HasFileSystemItem(items) ? ECS_ENABLED : ECS_DISABLED;
            return S_OK;
        }

        HRESULT STDMETHODCALLTYPE Invoke(IShellItemArray* items, IBindCtx*) override
        {
            return OpenFolderInDesktopTuner(items);
        }

        HRESULT STDMETHODCALLTYPE GetFlags(EXPCMDFLAGS* flags) override
        {
            if (flags == nullptr) return E_POINTER;
            *flags = ECF_DEFAULT;
            return S_OK;
        }

        HRESULT STDMETHODCALLTYPE EnumSubCommands(IEnumExplorerCommand** commands) override
        {
            if (commands == nullptr) return E_POINTER;
            *commands = nullptr;
            return E_NOTIMPL;
        }

    private:
        volatile long references_ = 1;
    };

    class ExplorerCommandFactory final : public IClassFactory
    {
    public:
        ExplorerCommandFactory() { InterlockedIncrement(&g_objectCount); }
        ~ExplorerCommandFactory() { InterlockedDecrement(&g_objectCount); }

        HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** object) override
        {
            if (object == nullptr) return E_POINTER;
            *object = nullptr;
            if (IsEqualIID(iid, IID_IUnknown) || IsEqualIID(iid, IID_IClassFactory))
            {
                *object = static_cast<IClassFactory*>(this);
                AddRef();
                return S_OK;
            }
            return E_NOINTERFACE;
        }

        ULONG STDMETHODCALLTYPE AddRef() override { return static_cast<ULONG>(InterlockedIncrement(&references_)); }
        ULONG STDMETHODCALLTYPE Release() override
        {
            const ULONG remaining = static_cast<ULONG>(InterlockedDecrement(&references_));
            if (remaining == 0) delete this;
            return remaining;
        }

        HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID iid, void** object) override
        {
            if (object == nullptr) return E_POINTER;
            *object = nullptr;
            if (outer != nullptr) return CLASS_E_NOAGGREGATION;
            auto command = new (std::nothrow) ExplorerCommand();
            if (command == nullptr) return E_OUTOFMEMORY;
            const HRESULT result = command->QueryInterface(iid, object);
            command->Release();
            return result;
        }

        HRESULT STDMETHODCALLTYPE LockServer(BOOL lock) override
        {
            if (lock) InterlockedIncrement(&g_serverLockCount);
            else InterlockedDecrement(&g_serverLockCount);
            return S_OK;
        }

    private:
        volatile long references_ = 1;
    };
}

STDAPI DllGetClassObject(REFCLSID classId, REFIID iid, void** object)
{
    if (!IsEqualCLSID(classId, CLSID_DesktopTunerExplorerCommand)) return CLASS_E_CLASSNOTAVAILABLE;
    if (object == nullptr) return E_POINTER;
    *object = nullptr;
    auto factory = new (std::nothrow) ExplorerCommandFactory();
    if (factory == nullptr) return E_OUTOFMEMORY;
    const HRESULT result = factory->QueryInterface(iid, object);
    factory->Release();
    return result;
}

STDAPI DllCanUnloadNow()
{
    return g_objectCount == 0 && g_serverLockCount == 0 ? S_OK : S_FALSE;
}
