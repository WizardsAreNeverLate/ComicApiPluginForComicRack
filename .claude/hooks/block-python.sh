#!/usr/bin/env bash
# PreToolUse guard for Bash and PowerShell: refuse to start a Python interpreter.
# Bare python waits on stdin and hangs the agent; python3 on this machine is the
# Windows Store stub. The port needs no interpreter: the oracle runs IronPython
# inside the test process.
cmd=$(jq -r '.tool_input.command // empty')
# A command starts at the line start, after ; & | ` or $(, or after a quote that follows -c or -Command.
# exec, env, nohup, Start-Process, timeout N, and VAR=value may come before the interpreter.
start='(^|[;&|`]|\$\(|-(c|Command)[[:space:]]+["'"'"'])'
prefix='([[:space:]]*(exec|env|nohup|Start-Process|timeout[[:space:]]+[0-9]+|[A-Za-z_][A-Za-z0-9_]*=[^[:space:]]*)[[:space:]]+)*'
interp='[[:space:]]*([^[:space:];&|"'"'"']*[/\\])?(python[0-9.]*|py|ipy(64)?)(\.exe)?(["'"'"'[:space:]]|$)'
if printf '%s\n' "$cmd" | grep -Eq "$start$prefix$interp"; then
	echo "Blocked: this project never runs a Python interpreter (python, python3, py, ipy). It hangs on stdin, and python3 here is the Windows Store stub. To check Python behaviour, write an oracle test that calls Oracle.Call; IronPython runs inside the test process." >&2
	exit 2
fi
exit 0
