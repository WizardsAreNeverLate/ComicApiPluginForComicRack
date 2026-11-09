#!/usr/bin/env bash
# PreToolUse guard for Bash and PowerShell: refuse to start a Python interpreter.
# Bare python waits on stdin and hangs the agent; python3 on this machine is the
# Windows Store stub. The port needs no interpreter: the oracle runs IronPython
# inside the test process.
cmd=$(jq -r '.tool_input.command // empty')
if printf '%s\n' "$cmd" | grep -Eq '(^|[;&|`]|\$\(|\bexec[[:space:]]|\btimeout[[:space:]]+[0-9]+[[:space:]])[[:space:]]*([^[:space:];&|"'"'"']*[/\\])?(python[0-9.]*|py|ipy(64)?)(\.exe)?([[:space:]]|$)'; then
	echo "Blocked: this project never runs a Python interpreter (python, python3, py, ipy). It hangs on stdin, and python3 here is the Windows Store stub. To check Python behaviour, write an oracle test that calls Oracle.Call; IronPython runs inside the test process." >&2
	exit 2
fi
exit 0
