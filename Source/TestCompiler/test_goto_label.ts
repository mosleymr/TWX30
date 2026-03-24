# Test GOTO with label
systemscript
goto :test_label
setvar %should_skip "This should be skipped"

:test_label
setvar %reached "Reached the label"
