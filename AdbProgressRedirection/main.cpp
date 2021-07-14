#include <windows.h>
#include <tchar.h>
#include <vector>
#include <string>
#include <numeric>
#include <iostream>
#include <algorithm>
#include <io.h>
#include <fcntl.h>

#define CAPTURE_INTERVAL_MS 100

static const COORD origin_coord = { 0, 0 };

HANDLE CreateInheritableConsoleHandle() {
	SECURITY_ATTRIBUTES security_attrs;
	security_attrs.nLength = sizeof(security_attrs);
	security_attrs.lpSecurityDescriptor = NULL;
	security_attrs.bInheritHandle = TRUE;
	return CreateConsoleScreenBuffer(
		GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, &security_attrs, CONSOLE_TEXTMODE_BUFFER, NULL);
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
	if (!CreateProcess(NULL, const_cast<LPWSTR>(cmd_line.c_str()), NULL, NULL, TRUE, 0, NULL, NULL, &startup_info, &proc_info))
	{
		return INVALID_HANDLE_VALUE;
	}

	CloseHandle(proc_info.hThread);
	return proc_info.hProcess;
}

int wmain(int argc, wchar_t* argv[], wchar_t* envp[]) {
	// If no command to execute
	if (argc <= 1) {
		return -1;
	}

	// Get the original stdout to pipe data to
	HANDLE stdout_handle = GetStdHandle(STD_OUTPUT_HANDLE);

	// Set stdout to UTF-16
	_setmode(_fileno(stdout), _O_U16TEXT);

	// Create a new console screen buffer
	HANDLE console_handle = CreateInheritableConsoleHandle();
	if (console_handle == INVALID_HANDLE_VALUE) {
		return -2;
	}

	// Replace stdout with the created console buffer so the child process will inherite it as its stdout
	if (!SetStdHandle(STD_OUTPUT_HANDLE, console_handle)) {
		CloseHandle(console_handle);
		return -3;
	}

	// Set console to UTF-16
	_setmode(_fileno(stdout), _O_U16TEXT);

	// Build child process command line for execution
	std::wstring command_line =
		std::accumulate(
			argv + 1,
			argv + argc,
			std::wstring{},
			[](const auto& cmd_line, const auto& curr_arg) {return cmd_line + L"\"" + curr_arg + L"\" "; });

	// Start child process
	HANDLE child_handle = CreateChildProcess(command_line);
	if (child_handle == INVALID_HANDLE_VALUE) {
		CloseHandle(console_handle);
		return -4;
	}

	// Replace stdout handle again to the original to pipe the output to it easily
	if (!SetStdHandle(STD_OUTPUT_HANDLE, stdout_handle)) {
		CloseHandle(child_handle);
		CloseHandle(console_handle);
		return -5;
	}

	// While the child process is running
	while (WaitForSingleObject(child_handle, 0) == WAIT_TIMEOUT) {
		// Read current line, where the cursor is
		auto line_str = ReadConsoleLine(console_handle, 0);
		if (std::none_of(line_str.begin(), line_str.end(), [](const auto& wch) { return !iswprint(wch); })) {
			std::wcout << line_str << std::endl;
			Sleep(CAPTURE_INTERVAL_MS);
		}
	}

	// Read previous line to get the final message
	auto line_str = ReadConsoleLine(console_handle, -1);
	if (std::none_of(line_str.begin(), line_str.end(), [](const auto& wch) { return !iswprint(wch); })) {
		std::wcout << line_str << std::endl;
	}

	// Fetch exit code from the child process and close it
	DWORD exit_code;
	if (!GetExitCodeProcess(child_handle, &exit_code)) {
		exit_code = -6;
	}

	// Close handles and return child exit code
	CloseHandle(child_handle);
	CloseHandle(console_handle);
	return exit_code;
}
