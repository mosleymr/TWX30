:start
# Test parenthesized compound expressions
getText CURRENTANSILINE $user_name (#27 & "[32mYou have a corporate memo from " & #27 & "[1;36m") (#27 & "[0;32m." & #13)
echo "Command compiled successfully"
halt
