# Test GOSUB and RETURN with labels
systemscript
setvar %before "Starting"
gosub :subroutine
setvar %after "After gosub"
halt

:subroutine
setvar %inside "Inside subroutine"
return
