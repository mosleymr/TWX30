setvar $name ""
setvar $age ""

echo "Testing GETINPUT command..."

getinput $name "Enter your name: "
echo "Name entered: " & $name

getinput $age "Enter your age: "
echo "Age entered: " & $age

echo "Test completed!"
halt
