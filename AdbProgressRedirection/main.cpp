#include <vector>
#include <string>
#include <numeric>
#include <iostream>
#include <algorithm>
#include <memory>
#include <type_traits>

#include <windows.h>
#include <tchar.h>
#include <io.h>
#include <fcntl.h>

#define CAPTURE_INTERVAL_MS 100

static const COORD origin_coord = { 0, 0 };

enum class ErrorType : uint8_t {
	NO_ARGS = 1,
	STDOUT_UTF16_SWITCH = 2,
	STDERR_UTF16_SWITCH = 3,
	CON_BUFF_CREATE = 4,
	CON_BUFF_SIZE = 5,
	CON_BUFF_SET = 6,
	CON_BUFF_UTF16_SWITCH = 7,
	CHILD_PROC_CREATE = 8,
	CHILD_PROC_EXIT_CODE = 9
};

int HandleError(ErrorType error_type) {
	std::wcerr << L"Error " << std::to_wstring(static_cast<std::underlying_type_t<ErrorType>>(error_type)) << std::endl;
	return (-static_cast<int>(error_type));
}

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
		0,
		static_cast<SHORT>(csbi.dwCursorPosition.Y + line_offset),
		static_cast<SHORT>(csbi.dwSize.X - 1),
		static_cast<SHORT>(csbi.dwCursorPosition.Y + line_offset) };

	std::vector<CHAR_INFO> char_data(csbi.dwSize.X);
	if (!ReadConsoleOutput(console_handle, &char_data[0], buf_size, origin_coord, &target)) {
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

HANDLE CreateChildProcess(const std::wstring& cmd, const std::wstring& args) {
	PROCESS_INFORMATION proc_info;
	STARTUPINFO startup_info;
	ZeroMemory(&startup_info, sizeof(startup_info));
	startup_info.cb = sizeof(startup_info);
	startup_info.dwFlags = STARTF_FORCEOFFFEEDBACK;
	if (!CreateProcess(
		const_cast<LPWSTR>(cmd.c_str()),
		const_cast<LPWSTR>(args.c_str()),
		NULL,
		NULL,
		TRUE,
		0,
		NULL,
		NULL,
		&startup_info,
		&proc_info)) {
		return INVALID_HANDLE_VALUE;
	}

	CloseHandle(proc_info.hThread);
	return proc_info.hProcess;
}

bool IsLinePrintable(const std::wstring& line) {
	return std::all_of(line.begin(), line.end(), [](const auto& wch) { return iswprint(wch); });
}

std::wstring TrimString(const std::wstring& string, const wchar_t* chars_to_trim = L" \t\n\r\v") {
	auto start_index = string.find_first_not_of(chars_to_trim);
	if (start_index == std::wstring::npos) {
		return L"";
	}

	auto end_index = string.find_last_not_of(chars_to_trim);
	return string.substr(start_index, end_index - start_index + 1);
}

int wmain(int argc, wchar_t* argv[], wchar_t* envp[]) {
	// If no command to execute
	if (argc <= 1) {
		return HandleError(ErrorType::NO_ARGS);  // No arguments to execute were provided!
	}

	// Get the original stdout to pipe data to
	HANDLE stdout_handle = GetStdHandle(STD_OUTPUT_HANDLE);

	// Set stdout to UTF-16
	fflush(stdout);
	int previous_stdout_mode = _setmode(_fileno(stdout), _O_U16TEXT);
	if (previous_stdout_mode == -1) {
		return HandleError(ErrorType::STDOUT_UTF16_SWITCH);  // Couldn't set STDOUT mode to UTF16!
	}

	// Set stderr to UTF-16
	fflush(stderr);
	int previous_stderr_mode = _setmode(_fileno(stderr), _O_U16TEXT);
	if (previous_stderr_mode == -1) {
		return HandleError(ErrorType::STDERR_UTF16_SWITCH);  // Couldn't set STDERR mode to UTF16!
	}

	// RAII guard to restore stdout's previous mode
	auto stdout_mode_restore = [](int* m) { _setmode(_fileno(stdout), *m); };
	auto stdout_mode_guard = MakeScopeFinallyGuard(&previous_stdout_mode, stdout_mode_restore);

	// RAII guard to restore stderr's previous mode
	auto stderr_mode_restore = [](int* m) { _setmode(_fileno(stderr), *m); };
	auto stderr_mode_guard = MakeScopeFinallyGuard(&previous_stderr_mode, stderr_mode_restore);

	// Create a new console screen buffer
	HANDLE console_handle = CreateInheritableConsoleHandle();
	if (console_handle == INVALID_HANDLE_VALUE) {
		return HandleError(ErrorType::CON_BUFF_CREATE);  // Couldn't create a console screen buffer!
	}

	// Resize it to prevent "..." lines
	if (SetConsoleScreenBufferSize(console_handle, COORD{ 1024, 1024 }) == 0) {
		return HandleError(ErrorType::CON_BUFF_SIZE);  // Couldn't change console screen size!
	}

	// RAII guard that will close the console handle automatically
	auto handle_closer = [](HANDLE* h) { CloseHandle(*h); };
	auto stdout_handle_guard = MakeScopeFinallyGuard(&console_handle, handle_closer);

	// Create a child process with inherited console screen buffer as its stdout
	HANDLE child_handle;
	{
		// Replace stdout with the created console buffer so the child process will inherite it as its stdout
		if (!SetStdHandle(STD_OUTPUT_HANDLE, console_handle)) {
			return HandleError(ErrorType::CON_BUFF_SET);  // Couldn't set STDOUT to the created console screen buffer!
		}

		// RAII guard that will restore stdout to the original one
		auto stdout_restore = [](HANDLE* h) { SetStdHandle(STD_OUTPUT_HANDLE, *h); };
		auto stdout_restore_guard = MakeScopeFinallyGuard(&stdout_handle, stdout_restore);

		// Set our created console to UTF-16
		fflush(stdout);
		if (_setmode(_fileno(stdout), _O_U16TEXT) == -1) {
			return HandleError(ErrorType::CON_BUFF_UTF16_SWITCH);  // Couldn't set the created console screen buffer's mode to UTF16!
		}

		// Build child process command line for execution
		std::wstring cmd_args =
			std::accumulate(
				argv + 1,
				argv + argc,
				std::wstring(),
				[](const auto& cmd_line, const auto& curr_arg) { return cmd_line + L"\"" + curr_arg + L"\" "; });

		// Start child process
		child_handle = CreateChildProcess(argv[1], cmd_args);
		if (child_handle == INVALID_HANDLE_VALUE) {
			return HandleError(ErrorType::CHILD_PROC_CREATE);  // Couldn't create a child process
		}
	}

	// RAII guard that will close the child handle automatically
	auto child_handle_guard = MakeScopeFinallyGuard(&child_handle, handle_closer);

	// While the child process is running
	std::wstring prev_line_str;
	while (WaitForSingleObject(child_handle, 0) == WAIT_TIMEOUT) {
		// Read current line, where the cursor is
		if (auto line_str = TrimString(ReadConsoleLine(console_handle, 0));
			(line_str != prev_line_str) && IsLinePrintable(line_str)) {
			std::wcout << line_str << std::endl;
			prev_line_str = line_str;
			Sleep(CAPTURE_INTERVAL_MS);
		}
	}

	// Read previous line to get the final message
	if (auto line_str = TrimString(ReadConsoleLine(console_handle, -1));
		(line_str != prev_line_str) && IsLinePrintable(line_str)) {
		std::wcout << line_str << std::endl;
	}

	// Fetch exit code from the child process to return it
	DWORD exit_code;
	if (!GetExitCodeProcess(child_handle, &exit_code)) {
		return HandleError(ErrorType::CHILD_PROC_EXIT_CODE);  // Couldn't get child process return code!
	}

	return exit_code;
}
