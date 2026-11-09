#!/usr/bin/env bash
# Runs block-python.sh against commands it must block (exit 2) and must allow (exit 0).
# Usage: bash .claude/hooks/test-block-python.sh
hook="$(dirname "$0")/block-python.sh"
failures=0
check() {
	expected=$1
	printf '%s' "$2" | jq -Rs '{tool_name:"Bash",tool_input:{command:.}}' | bash "$hook" >/dev/null 2>&1
	actual=$?
	if [ "$actual" -ne "$expected" ]; then
		echo "FAIL expected $expected got $actual :: $2"
		failures=$((failures + 1))
	fi
}
block() { check 2 "$1"; }
allow() { check 0 "$1"; }

block 'python'
block 'python -'
block 'python3 -I -'
block 'py -3 script.py'
block 'ipy.exe foo.py'
block 'cd x && python3 --version'
block 'echo hi; python'
block '/c/Users/jason/AppData/Local/Microsoft/WindowsApps/python3.exe -'
block '& python -c "print(1)"'
block 'timeout 5 python'
block 'x=$(python -V)'
block $'ls\npython - <<EOF\nEOF'
block 'a | python'

allow 'ls src/py/utils'
allow 'cat tests/py/cvoracle.py'
allow 'grep -n python README.md'
allow 'echo "never run python"'
allow './Build.ps1 -Rebuild'
allow 'log.sh x "the label (Python stripped the localized label) goes"'
allow 'git commit -m "docs: Python behaviour"'
allow 'git grep -n "PyStr.Lower"'

if [ "$failures" -eq 0 ]; then
	echo "all hook cases passed"
else
	echo "$failures hook case(s) failed"
	exit 1
fi
