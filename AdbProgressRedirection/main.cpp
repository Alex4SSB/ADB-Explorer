#include <vector>
#include <string>
#include <numeric>
#include <iostream>
#include <algorithm>

#define _WINDOWS_
#define _INC_WINDOWS
#define _AMD64_
#define UNICODE

#include <windef.h>
#include <winbase.h>
#include <wincon.h>
#include <fcntl.h>
#include <corecrt_io.h>

#define CAPTURE_INTERVAL_MS 100

static const COORD origin_coord = { 0, 0 };

template<typename T, typename D>
auto MakeScopeFinallyGuard(T* data, D&& deleter) { return std::unique_ptr<T, D>(data, std::forward<D>(deleter)); }

HANDLE CreateInheritableConsoleHandle() {
	SECURITY_ATTRIBUTES security_attrs;
	security_attrs.nLength = sizeof(security_attrs);
	security_attrs.lpSecurityDescriptor = NULL;
	security_attrs.bInheritHandle = TRUE;
	return CreateConsoleScreenBuffer(
		GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
		&security_attrs,
		CONSOLE_TEXTMODE_BUFFER, NULL);
}

std::wstring ReadConsoleLine(HANDLE console_handle, SHORT line_offset) {
	// Get console buffer info
	CONSOLE_SCREEN_BUFFER_INFO csbi;
	if (!GetConsoleScreenBufferInfo(console_handle, &csbi)) {
		return {};
	}

	// Read the whole current line of the screen buffer
	COORD buf_size = { csbi.dwSize.X, 1 };
	SMALL_RECT target = {
		0, csbi.dwCursorPosition.Y + line_offset, csbi.dwSize.X - 1, csbi.dwCursorPosition.Y + line_offset };

	std::vector<CHAR_INFO> char_data(csbi.dwSize.X);
	if (!ReadConsoleOutputW(console_handle, &char_data[0], buf_size, origin_coord, &target)) {
		return {};
	}

	// Convert screen buffer content to a wide-string and return it
	std::wstring out_str;
	out_str.reserve(char_data.size());

	std::transform(
		char_data.begin(),
		char_data.end(),
		std::back_inserter(out_str),
		[](const auto& char_info) { return char_info.Char.UnicodeChar; });

	return out_str;
}

HANDLE CreateChildProcess(const std::wstring& cmd_line) {
	PROCESS_INFORMATION proc_info;
	STARTUPINFO startup_info;
	ZeroMemory(&startup_info, sizeof(startup_info));
	startup_info.cb = sizeof(startup_info);
	startup_info.dwFlags = STARTF_FORCEOFFFEEDBACK;
	if (!CreateProcess(
		NULL, const_cast<LPWSTR>(cmd_line.c_str()), NULL, NULL, TRUE, 0, NULL, NULL, &startup_info, &proc_info))
	{
		return INVALID_HANDLE_VALUE;
	}

	CloseHandle(proc_info.hThread);
	return proc_info.hProcess;
}

bool IsLinePrintable(const std::wstring& line) {
	return std::all_of(line.begin(), line.end(), [](const auto& wch) { return iswprint(wch); });
}

int wmain(int argc, wchar_t* argv[], wchar_t* envp[]) {
	// If no command to execute
	if (argc <= 1) {
		std::wcerr << L"No arguments to execute were provided!" << std::endl;
		return -1;
	}

	// Get the original stdout to pipe data to
	HANDLE stdout_handle = GetStdHandle(STD_OUTPUT_HANDLE);

	// Set stdout to UTF-16
	fflush(stdout);
	int previous_stdout_mode = _setmode(_fileno(stdout), _O_U16TEXT);
	if (previous_stdout_mode == -1) {
		std::wcerr << L"Couldn't set STDOUT mode to UTF16!" << std::endl;
		return -1;
	}

	// Set stderr to UTF-16
	fflush(stderr);
	int previous_stderr_mode = _setmode(_fileno(stderr), _O_U16TEXT);
	if (previous_stderr_mode == -1) {
		std::wcerr << L"Couldn't set STDERR mode to UTF16!" << std::endl;
		return -1;
	}

#pragma warning(push)
#pragma warning(disable: 6031)
	// RAII guard to restore stdout's previous mode
	auto stdout_mode_restore = [](int* m) { _setmode(_fileno(stdout), *m); };
	auto stdout_mode_guard = MakeScopeFinallyGuard(&previous_stdout_mode, stdout_mode_restore);

	// RAII guard to restore stderr's previous mode
	auto stderr_mode_restore = [](int* m) { _setmode(_fileno(stderr), *m); };
	auto stderr_mode_guard = MakeScopeFinallyGuard(&previous_stderr_mode, stderr_mode_restore);

#pragma warning(pop)

	// Create a new console screen buffer
	HANDLE console_handle = CreateInheritableConsoleHandle();
	if (console_handle == INVALID_HANDLE_VALUE) {
		std::wcerr << L"Couldn't create a console screen buffer!" << std::endl;
		return -1;
	}

	// Resize it to prevent "..." lines
	if (SetConsoleScreenBufferSize(console_handle, COORD{ 1024, 1024 }) == 0) {
		std::wcerr << L"Couldn't change console screen size!" << std::endl;
		return -1;
	}

	// RAII guard that will close the console handle automatically
	auto handle_closer = [](HANDLE* h) { CloseHandle(*h); };
	auto stdout_handle_guard = MakeScopeFinallyGuard(&console_handle, handle_closer);

	// Create a child process with inherited console screen buffer as its stdout
	HANDLE child_handle;
	{
		// Replace stdout with the created console buffer so the child process will inherite it as its stdout
		if (!SetStdHandle(STD_OUTPUT_HANDLE, console_handle)) {
			std::wcerr << L"Couldn't set STDOUT to the created console screen buffer!" << std::endl;
			return -1;
		}

		// RAII guard that will restore stdout to the original one
		auto stdout_restore = [](HANDLE* h) { SetStdHandle(STD_OUTPUT_HANDLE, *h); };
		auto stdout_restore_guard = MakeScopeFinallyGuard(&stdout_handle, stdout_restore);

		// Set our created console to UTF-16
		fflush(stdout);
		if (_setmode(_fileno(stdout), _O_U16TEXT) == -1) {
			std::wcerr << L"Couldn't set the created console screen buffer's mode to UTF16!" << std::endl;
			return -1;
		}

		// Build child process command line for execution
		std::wstring command_line =
			std::accumulate(
				argv + 1,
				argv + argc,
				std::wstring{},
				[](const auto& cmd_line, const auto& curr_arg) { return cmd_line + L"\"" + curr_arg + L"\" "; });

		// Start child process
		child_handle = CreateChildProcess(command_line);
		if (child_handle == INVALID_HANDLE_VALUE) {
			std::wcerr << L"Couldn't create a child process for: " << command_line << std::endl;
			return -1;
		}
	}

	// RAII guard that will close the child handle automatically
	auto child_handle_guard = MakeScopeFinallyGuard(&child_handle, handle_closer);

	// While the child process is running
	std::wstring prev_line_str;
	while (WaitForSingleObject(child_handle, 0) == WAIT_TIMEOUT) {
		// Read current line, where the cursor is
		auto line_str = ReadConsoleLine(console_handle, 0);
		if ((line_str != prev_line_str) && IsLinePrintable(line_str)) {
			std::wcout << line_str << std::endl;
			prev_line_str = line_str;
			Sleep(CAPTURE_INTERVAL_MS);
		}
	}

	// Read previous line to get the final message
	auto line_str = ReadConsoleLine(console_handle, -1);
	if ((line_str != prev_line_str) && IsLinePrintable(line_str)) {
		std::wcout << line_str << std::endl;
	}

	// Fetch exit code from the child process to return it
	DWORD exit_code;
	if (!GetExitCodeProcess(child_handle, &exit_code)) {
		std::wcerr << L"Couldn't get child process return code!" << std::endl;
		return -1;
	}

	return exit_code;
}
