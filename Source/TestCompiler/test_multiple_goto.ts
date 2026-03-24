# Simpler script - just multiple GOTOs
systemscript  
goto :first
setvar %skip1 "skipped"

:first
goto :second  
setvar %skip2 "also skipped"

:second
setvar %reached "made it"
halt
