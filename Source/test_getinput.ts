// Test GETINPUT command
echo "Testing GETINPUT command"
getinput $name "Enter your name: "
echo "You entered: " & $name
getinput $age "Enter your age: "
echo "Age: " & $age
echo "Test complete"
