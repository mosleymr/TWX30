:start
loadVar $botIsDeaf
if ($botIsDeaf = TRUE)
    gosub :donePrefer
end
echo "Test complete"
halt

:donePrefer
echo "In donePrefer subroutine"
return
