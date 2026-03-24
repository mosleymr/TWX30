:start
setvar $sector 100
if (SECTOR.WARPS[CURRENTSECTOR][$sector] > 0)
  echo "Has warp"
end
halt
