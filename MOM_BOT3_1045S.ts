systemscript
reqrecording
goto :LOAD_BOT

:CHECKSTARTINGPROMPT
setvar $STARTINGLOCATION $CURRENT_PROMPT
getwordpos " "&$VALIDPROMPTS&" " $POS $STARTINGLOCATION
if ($POS <= 0)
	setvar $MESSAGE "Invalid starting prompt: ["&$CURRENT_PROMPT&"]. Valid prompt(s) for this command: ["&$VALIDPROMPTS&"]*"
	gosub :SWITCHBOARD
	goto :WAIT_FOR_COMMAND
end
return

:QUIKSTATS
setvar $CURRENT_PROMPT "Undefined"
killtrigger NOPROMPT
killtrigger PROMPT
killtrigger STATLINETRIG
killtrigger GETLINE2
settextlinetrigger PROMPT :ALLPROMPTS #145&#8
settextlinetrigger STATLINETRIG :STATSTART #179
send #145&"/"
pause

:ALLPROMPTS
getword CURRENTLINE $CURRENT_PROMPT 1
striptext $CURRENT_PROMPT #145
striptext $CURRENT_PROMPT #8
settextlinetrigger PROMPT :ALLPROMPTS #145&#8
pause

:STATSTART
killtrigger PROMPT
setvar $STATS ""
setvar $WORDY ""

:STATSLINE
killtrigger STATLINETRIG
killtrigger GETLINE2
setvar $LINE2 CURRENTLINE
replacetext $LINE2 #179 " "
striptext $LINE2 ","
setvar $STATS $STATS&$LINE2
getwordpos $LINE2 $POS "Ship"
if ($POS > 0)
	goto :GOTSTATS
else
	settextlinetrigger GETLINE2 :STATSLINE
	pause
end

:GOTSTATS
setvar $STATS $STATS&" @@@"
setvar $CURRENT_WORD 0
while ($WORDY <> "@@@")
	if ($WORDY = "Sect")
		getword $STATS $CURRENT_SECTOR ($CURRENT_WORD + 1)
	elseif ($WORDY = "Turns")
		getword $STATS $TURNS ($CURRENT_WORD + 1)
	elseif ($WORDY = "Creds")
		getword $STATS $CREDITS ($CURRENT_WORD + 1)
	elseif ($WORDY = "Figs")
		getword $STATS $FIGHTERS ($CURRENT_WORD + 1)
	elseif ($WORDY = "Shlds")
		getword $STATS $SHIELDS ($CURRENT_WORD + 1)
	elseif ($WORDY = "Hlds")
		getword $STATS $TOTAL_HOLDS ($CURRENT_WORD + 1)
	elseif ($WORDY = "Ore")
		getword $STATS $ORE_HOLDS ($CURRENT_WORD + 1)
	elseif ($WORDY = "Org")
		getword $STATS $ORGANIC_HOLDS ($CURRENT_WORD + 1)
	elseif ($WORDY = "Equ")
		getword $STATS $EQUIPMENT_HOLDS ($CURRENT_WORD + 1)
	elseif ($WORDY = "Col")
		getword $STATS $COLONIST_HOLDS ($CURRENT_WORD + 1)
	elseif ($WORDY = "Phot")
		getword $STATS $PHOTONS ($CURRENT_WORD + 1)
	elseif ($WORDY = "Armd")
		getword $STATS $ARMIDS ($CURRENT_WORD + 1)
	elseif ($WORDY = "Lmpt")
		getword $STATS $LIMPETS ($CURRENT_WORD + 1)
	elseif ($WORDY = "GTorp")
		getword $STATS $GENESIS ($CURRENT_WORD + 1)
	elseif ($WORDY = "TWarp")
		getword $STATS $TWARP_TYPE ($CURRENT_WORD + 1)
	elseif ($WORDY = "Clks")
		getword $STATS $CLOAKS ($CURRENT_WORD + 1)
	elseif ($WORDY = "Beacns")
		getword $STATS $BEACONS ($CURRENT_WORD + 1)
	elseif ($WORDY = "AtmDt")
		getword $STATS $ATOMIC ($CURRENT_WORD + 1)
	elseif ($WORDY = "Corbo")
		getword $STATS $CORBO ($CURRENT_WORD + 1)
	elseif ($WORDY = "EPrb")
		getword $STATS $EPROBES ($CURRENT_WORD + 1)
	elseif ($WORDY = "MDis")
		getword $STATS $MINE_DISRUPTORS ($CURRENT_WORD + 1)
	elseif ($WORDY = "PsPrb")
		getword $STATS $PSYCHIC_PROBE ($CURRENT_WORD + 1)
	elseif ($WORDY = "PlScn")
		getword $STATS $PLANET_SCANNER ($CURRENT_WORD + 1)
	elseif ($WORDY = "LRS")
		getword $STATS $SCAN_TYPE ($CURRENT_WORD + 1)
	elseif ($WORDY = "Aln")
		getword $STATS $ALIGNMENT ($CURRENT_WORD + 1)
	elseif ($WORDY = "Exp")
		getword $STATS $EXPERIENCE ($CURRENT_WORD + 1)
	elseif ($WORDY = "Corp")
		getword $STATS $CORP ($CURRENT_WORD + 1)
	elseif ($WORDY = "Ship")
		getword $STATS $SHIP_NUMBER ($CURRENT_WORD + 1)
	end
	add $CURRENT_WORD 1
	getword $STATS $WORDY $CURRENT_WORD
end

:DONEQUIKSTATS
killtrigger STATLINETRIG
killtrigger GETLINE2
return

:KILLTHETRIGGERS
killalltriggers
setdelaytrigger UNFREEZINGTRIGGER :UNFREEZEBOT 100000
return

:RELOG_FREEZE_TRIGGER
killtrigger UNFREEZINGTRIGGER
killtrigger UNFREEZINGTRIGGERBIGDELAY
setdelaytrigger UNFREEZINGTRIGGER :VERIFYDELAY 30000
return

:BIGDELAY_KILLTHETRIGGERS
killalltriggers
setdelaytrigger UNFREEZINGTRIGGERBIGDELAY :UNFREEZEBOT 1800000
return

:UNFREEZEBOT
echo "*Bot timed out, unfreezing..*"
send "'{" $BOT_NAME "} - Bot frozen for over 100 seconds, resetting...*"
goto :WAIT_FOR_COMMAND

:GETPLANETINFO
send "*"
settextlinetrigger PLANETINFO :PLANETINFO "Planet #"
pause

:PLANETINFO
setvar $CITADEL 0
setvar $SECTOR_CANNON 0
setvar $ATMOSPHERE_CANNON 0
setvar $CITADEL_CREDITS 0
getword CURRENTLINE $PLANET 2
striptext $PLANET "#"
getword CURRENTLINE $CURRENT_SECTOR 5
striptext $CURRENT_SECTOR ":"
waiton "2 Build 1   Product    Amount     Amount     Maximum"

:GETPLANETSTUFF
settextlinetrigger FUELSTART :FUELSTART "Fuel Ore"
settextlinetrigger ORGSTART :ORGSTART "Organics"
settextlinetrigger EQUIPSTART :EQUIPSTART "Equipment"
settextlinetrigger FIGSTART :FIGSTART "Fighters        N/A"
settextlinetrigger CITADELSTART :CITADELSTART "Planet has a level"
settextlinetrigger CANNON :CANNONSTART ", AtmosLvl="
settexttrigger PLANETINFODONE :PLANETINFODONE "Planet command (?=help)"
pause

:FUELSTART
getword CURRENTLINE $PLANET_FUEL 6
getword CURRENTLINE $PLANET_FUEL_MAX 8
striptext $PLANET_FUEL ","
striptext $PLANET_FUEL_MAX ","
pause

:ORGSTART
getword CURRENTLINE $PLANET_ORGANICS 5
getword CURRENTLINE $PLANET_ORGANICS_MAX 7
striptext $PLANET_ORGANICS ","
striptext $PLANET_ORGANICS_MAX ","
pause

:EQUIPSTART
getword CURRENTLINE $PLANET_EQUIPMENT 5
getword CURRENTLINE $PLANET_EQUIPMENT_MAX 7
striptext $PLANET_EQUIPMENT ","
striptext $PLANET_EQUIPMENT_MAX ","
pause

:FIGSTART
getword CURRENTLINE $PLANET_FIGHTERS 5
getword CURRENTLINE $PLANET_FIGHTERS_MAX 7
striptext $PLANET_FIGHTERS ","
striptext $PLANET_FIGHTERS_MAX ","
pause

:CITADELSTART
getword CURRENTLINE $CITADEL 5
getword CURRENTLINE $CITADEL_CREDITS 9
striptext $CITADEL_CREDITS ","
pause

:CANNONSTART
getword CURRENTLINE $ATMOSPHERE_CANNON 5
getword CURRENTLINE $SECTOR_CANNON 6
striptext $SECTOR_CANNON "SectLvl="
striptext $SECTOR_CANNON "%"
striptext $ATMOSPHERE_CANNON "AtmosLvl="
striptext $ATMOSPHERE_CANNON "%"
striptext $ATMOSPHERE_CANNON ","
pause

:PLANETINFODONE
killtrigger CITADELSTART
killtrigger CANNON
return

:REMOVEFIGFROMDATA
getsectorparameter $TARGET "FIGSEC" $CHECK
if ($CHECK = TRUE)
	getsectorparameter 2 "FIG_COUNT" $FIGCOUNT
	setsectorparameter 2 "FIG_COUNT" ($FIGCOUNT - 1)
end
setsectorparameter $TARGET "FIGSEC" FALSE
return

:ADDFIGTODATA
setsectorparameter $TARGET "FIGSEC" TRUE
return

:GETINFO
setvar $NOFLIP FALSE
setvar $PHOTONS 0
setvar $SCAN_TYPE "None"
setvar $TWARP_TYPE 0
setvar $CORPSTRING "[0]"
setvar $IGSTAT 0
settextlinetrigger GETINFO_CN9_CHECK_1 :GETINFO_CN9_CHECK "<N> Interdictor Control"
settextlinetrigger GETINFO_CN9_CHECK_2 :GETINFO_CN9_CHECK "<N> Move to NavPoint"

:WAITONINFO
send "?I"
waiton "<Info>"
settextlinetrigger GETTRADERNAME :GETTRADERNAME "Trader Name    :"
settextlinetrigger GETEXPANDALIGN :GETEXPANDALIGN "Rank and Exp"
settextlinetrigger GETCORP :GETCORP "Corp           #"
settextlinetrigger GETSHIPTYPE :GETSHIPTYPE "Ship Info      :"
settextlinetrigger GETTPW :GETTPW "Turns to Warp  :"
settextlinetrigger GETSECT :GETSECT "Current Sector :"
settextlinetrigger GETTURNS :GETTURNS "Turns left"
settextlinetrigger GETHOLDS :GETHOLDS "Total Holds"
settextlinetrigger GETFIGHTERS :GETFIGHTERS "Fighters       :"
settextlinetrigger GETSHIELDS :GETSHIELDS "Shield points  :"
settextlinetrigger GETPHOTONS :GETPHOTONS "Photon Missiles:"
settextlinetrigger GETSCANTYPE :GETSCANTYPE "LongRange Scan :"
settextlinetrigger GETTWARPTYPE1 :GETTWARPTYPE1 "  (Type 1 Jump):"
settextlinetrigger GETTWARPTYPE2 :GETTWARPTYPE2 "  (Type 2 Jump):"
settextlinetrigger GETCREDITS :GETCREDITS "Credits"
settextlinetrigger CHECKIG :CHECKIG "Interdictor ON :"
settexttrigger GETINFODONE :GETINFODONE "Command [TL="
settexttrigger GETINFODONE2 :GETINFODONE "Citadel command"
pause

:GETINFO_CN9_CHECK
setvar $NOFLIP TRUE
pause

:GETTRADERNAME
killtrigger GETINFO_CN9_CHECK_1
killtrigger GETINFO_CN9_CHECK_2
setvar $TRADER_NAME CURRENTLINE
striptext $TRADER_NAME "Trader Name    : "
setvar $I 1
while ($I <= $RANKSLENGTH)
	setvar $TEMP $RANKS[$I]
	striptext $TEMP "31m"
	striptext $TEMP "36m"
	striptext $TRADER_NAME $TEMP&" "
	add $I 1
end
pause

:GETEXPANDALIGN
getword CURRENTLINE $EXPERIENCE 5
getword CURRENTLINE $ALIGNMENT 7
striptext $EXPERIENCE ","
striptext $ALIGNMENT ","
striptext $ALIGNMENT "Alignment="
pause

:GETCORP
getword CURRENTLINE $CORP 3
striptext $CORP ","
setvar $CORPSTRING "["&$CORP&"]"
pause

:GETSHIPTYPE
getwordpos CURRENTLINE $SHIPTYPEEND "Ported="
subtract $SHIPTYPEEND 18
cuttext CURRENTLINE $SHIP_TYPE 18 $SHIPTYPEEND
pause

:GETTPW
getword CURRENTLINE $TURNS_PER_WARP 5
pause

:GETSECT
getword CURRENTLINE $CURRENT_SECTOR 4
pause

:GETTURNS
getword CURRENTLINE $TURNS 4
if ($TURNS = "Unlimited")
	setvar $TURNS 65000
	setvar $UNLIMITEDGAME TRUE
end
savevar $UNLIMITEDGAME
pause

:GETHOLDS
setvar $TEMP CURRENTLINE&" "
gettext $TEMP $ORE_HOLDS "Ore=" " "
if ($ORE_HOLDS = "")
	setvar $ORE_HOLDS 0
end
gettext $TEMP $ORGANIC_HOLDS "Organics=" " "
if ($ORGANIC_HOLDS = "")
	setvar $ORGANIC_HOLDS 0
end
gettext $TEMP $EQUIPMENT_HOLDS "Equipment=" " "
if ($EQUIPMENT_HOLDS = "")
	setvar $EQUIPMENT_HOLDS 0
end
gettext $TEMP $COLONIST_HOLDS "Colonists=" " "
if ($COLONIST_HOLDS = "")
	setvar $COLONIST_HOLDS 0
end
gettext $TEMP $EMPTY_HOLDS "Empty=" " "
if ($EMPTY_HOLDS = "")
	setvar $EMPTY_HOLDS 0
end
pause

:GETFIGHTERS
getword CURRENTLINE $FIGHTERS 3
striptext $FIGHTERS ","
pause

:GETSHIELDS
getword CURRENTLINE $SHIELDS 4
striptext $SHIELDS ","
pause

:GETPHOTONS
getword CURRENTLINE $PHOTONS 3
pause

:GETSCANTYPE
getword CURRENTLINE $SCAN_TYPE 4
pause

:GETTWARPTYPE1
getword CURRENTLINE $TWARP_1_RANGE 4
setvar $TWARP_TYPE 1
pause

:GETTWARPTYPE2
getword CURRENTLINE $TWARP_2_RANGE 4
setvar $TWARP_TYPE 2
pause

:GETCREDITS
getword CURRENTLINE $CREDITS 3
striptext $CREDITS ","
if ($IGSTAT = 0)
	setvar $IGSTAT "NO IG"
end
pause

:CHECKIG
getword CURRENTLINE $IGSTAT 4
pause

:GETINFODONE
killalltriggers
return

:GETCOST
setvar $LSD_COST 0
getwordpos CURRENTLINE $LSD_POS "="
if ($LSD_POS <> 0)
	cuttext CURRENTLINE $LSD_COST ($LSD_POS + 1) 999
	striptext $LSD_COST " cr"
end
return

:CURRENT_PROMPT
settexttrigger PROMPT :ALLPROMPTSCATCH #145&#8
setdelaytrigger PROMPT_DELAY :CURRENT_PROMPT_DELAY 5000
send #145
pause

:CURRENT_PROMPT_DELAY
settextouttrigger ATKEYS :CURRENT_PROMPT_AT_KEYS
setdelaytrigger PROMPT_DELAY :VERIFYDELAY 30000
pause

:CURRENT_PROMPT_AT_KEYS
getouttext $OUT
send $OUT
goto :WAIT_FOR_COMMAND

:ALLPROMPTSCATCH
killtrigger PROMPT_DELAY
getword CURRENTLINE $CURRENT_PROMPT 1
if ($CURRENT_PROMPT = 0)
	getword CURRENTANSILINE $CURRENT_PROMPT 1
end
striptext $CURRENT_PROMPT #145
striptext $CURRENT_PROMPT #8
return

:SURROUND
gosub :KILLTHETRIGGERS
gosub :QUIKSTATS
if ($PHOTONS > 0)
	if ($SHIPPHOTONCHECK = $SHIP_NUMBER)
	else
		setvar $SHIPPHOTONCHECK $SHIP_NUMBER
		echo "*"&ANSI_14&"You are carrying photons. *If you wish to surround anyway, press TAB-S again.*"&ANSI_7
		goto :WAIT_FOR_COMMAND
	end
end
setvar $STARTINGLOCATION $CURRENT_PROMPT
if ($STARTINGLOCATION = "Command")
elseif ($STARTINGLOCATION = "Citadel")
	send "q "
	gosub :GETPLANETINFO
	send "q "
elseif ($STARTINGLOCATION = "Planet")
	gosub :GETPLANETINFO
	send "q "
else
	echo "*Wrong prompt for surround command.*"
	goto :WAIT_FOR_COMMAND
end

:STARTSURROUND
send "szh* "
settexttrigger SURROUNDSECTOR :CONTINUESURROUNDSECTOR "["&$CURRENT_SECTOR&"]"
pause

:CONTINUESURROUNDSECTOR
gosub :GETSHIPSTATS
if ($SHIP_MAX_ATTACK > $FIGHTERS)
	setvar $SHIP_MAX_ATTACK ($FIGHTERS / 2)
end
setarray $RND_SURROUND SECTOR.WARPCOUNT[$CURRENT_SECTOR]
setvar $I 1
setvar $SURROUNDOUTPUT ""
setvar $YOUROWNCOUNT 0
while (SECTOR.WARPS[$CURRENT_SECTOR][$I] > 0)
	setvar $SURROUNDSTRING ""
	setvar $ADJ_SEC SECTOR.WARPS[$CURRENT_SECTOR][$I]
	getdistance $DISTANCE $ADJ_SEC $CURRENT_SECTOR
	if ($DISTANCE <= 0)
		send "^f"&$ADJ_SEC&"*"&$CURRENT_SECTOR&"*q"
		waiton "ENDINTERROG"
		getdistance $DISTANCE $ADJ_SEC $CURRENT_SECTOR
	end
	setvar $CONTAINSSHIELDEDPLANET FALSE
	setvar $P 1
	while ($P <= SECTOR.PLANETCOUNT[$ADJ_SEC])
		getword SECTOR.PLANETS[$ADJ_SEC][$P] $TEST 1
		if ($TEST = "<<<<")
			setvar $CONTAINSSHIELDEDPLANET TRUE
		end
		add $P 1
	end
	setvar $TEMPOFFODD $SHIP_OFFENSIVE_ODDS
	multiply $TEMPOFFODD $SHIP_MAX_ATTACK
	divide $TEMPOFFODD 12
	setvar $FIGOWNER SECTOR.FIGS.OWNER[$ADJ_SEC]
	setvar $MINEOWNER SECTOR.MINES.OWNER[$ADJ_SEC]
	setvar $LIMPOWNER SECTOR.LIMPETS.OWNER[$ADJ_SEC]
	if (($SURROUNDOVERWRITE = FALSE) and (($FIGOWNER = "belong to your Corp") or ($FIGOWNER = "yours")))
		add $YOUROWNCOUNT 1
		if ($YOUROWNCOUNT = $TOTALWARPS)
			setvar $SURROUNDOUTPUT $SURROUNDOUTPUT&"(Surround) All sectors around are friendly fighters.*"
		end
	elseif (SECTOR.FIGS.QUANTITY[$ADJ_SEC] >= $TEMPOFFODD)
		setvar $SURROUNDOUTPUT $SURROUNDOUTPUT&"(Surround) Too many fighters in sector "&$ADJ_SEC&".*"
	elseif (($ADJ_SEC <= 10) or ($ADJ_SEC = $STARDOCK))
		setvar $SURROUNDOUTPUT $SURROUNDOUTPUT&"(Surround) Avoided Fed Space, sector "&$ADJ_SEC&".*"
	elseif ((SECTOR.PLANETCOUNT[$ADJ_SEC] > 0) and $SURROUNDAVOIDALLPLANETS)
		setvar $SURROUNDOUTPUT $SURROUNDOUTPUT&"(Surround) Avoided planet in sector "&$ADJ_SEC&".*"
	elseif ($CONTAINSSHIELDEDPLANET and $SURROUNDAVOIDSHIELDEDONLY)
		setvar $SURROUNDOUTPUT $SURROUNDOUTPUT&"(Surround) Avoided shielded planet in sector "&$ADJ_SEC&".*"
	elseif ($DISTANCE > 1)
		setvar $SURROUNDOUTPUT $SURROUNDOUTPUT&"(Surround) Avoided one way in sector "&$ADJ_SEC&".*"
	elseif (($SURROUNDPASSIVE = TRUE) and (((SECTOR.ANOMALY[$ADJ_SEC] = TRUE) and (($LIMPOWNER <> "belong to your Corp") and ($LIMPOWNER <> "yours"))) or (SECTOR.FIGS.QUANTITY[$ADJ_SEC] > 0) or ((SECTOR.MINES.QUANTITY[$ADJ_SEC] > 0) and (($MINEOWNER <> "belong to your Corp") and ($MINEOWNER <> "yours")))))
		setvar $SURROUNDOUTPUT $SURROUNDOUTPUT&"(Surround) Avoided non-passive situation in sector "&$ADJ_SEC&".*"
	else
		if ($DROPOFFENSIVE)
			setvar $DEPLOYFIG "o"
		elseif ($DROPTOLL)
			setvar $DEPLOYFIG "t"
		else
			setvar $DEPLOYFIG "d"
		end
		setvar $SURROUNDSTRING $SURROUNDSTRING&" m z "&$ADJ_SEC&"* z a "&$SHIP_MAX_ATTACK&"* * "
		if (($SURROUNDFIGS > 0) and ($FIGHTERS > $SURROUNDFIGS))
			setvar $SURROUNDSTRING $SURROUNDSTRING&"f z"&$SURROUNDFIGS&"*zc"&$DEPLOYFIG&"*  "
			subtract $FIGHTERS $SURROUNDFIGS
			setvar $TARGET $ADJ_SEC
			gosub :ADDFIGTODATA
		end
		if (($SURROUNDLIMP > 0) and (($LIMPETS > $SURROUNDLIMP) and ($LIMPETS > 0)))
			setvar $SURROUNDSTRING $SURROUNDSTRING&"h2 z"&$SURROUNDLIMP&"*zc* "
			subtract $LIMPETS $SURROUNDLIMP
			setsectorparameter $ADJ_SEC "LIMPSEC" TRUE
		end
		if (($SURROUNDMINE > 0) and (($ARMIDS > $SURROUNDMINE) and ($ARMIDS > 0)))
			setvar $SURROUNDSTRING $SURROUNDSTRING&"h1 z"&$SURROUNDMINE&"*zc* "
			subtract $ARMIDS $SURROUNDMINE
			setsectorparameter $ADJ_SEC "MINESEC" TRUE
		end
		setvar $SURROUNDSTRING $SURROUNDSTRING&"m z"&$CURRENT_SECTOR&"* "
		setvar $SURROUNDSTRING $SURROUNDSTRING&"za "&$SHIP_MAX_ATTACK&"* * "
	end
	setvar $RND_SURROUND[$I] $SURROUNDSTRING&"m z"&$CURRENT_SECTOR&"* za "&$SHIP_MAX_ATTACK&"* * "
	add $I 1
end
setvar $RND_IDX 1
setvar $RND_ADJ1 " "
setvar $RND_STRING " "
while ($RND_IDX <= SECTOR.WARPCOUNT[$CURRENT_SECTOR])
	setvar $RND_ADJ1 $RND_ADJ1&" "&$RND_IDX
	add $RND_IDX 1
end
setvar $RND_ADJ1 $RND_ADJ1&" "
setvar $RND_IDX 1
while ($RND_IDX <= 100)
	setvar $RND_ADJ $RND_ADJ1
	setvar $RND_RESULT ""
	setvar $RND_WARPS SECTOR.WARPCOUNT[$CURRENT_SECTOR]
	while ($RND_WARPS > 0)
		getrnd $RND_PTR 1 $RND_WARPS
		getword $RND_ADJ $RND_TEMP $RND_PTR
		replacetext $RND_ADJ " "&$RND_TEMP&" " " "
		subtract $RND_WARPS 1
		setvar $RND_RESULT $RND_RESULT&$RND_TEMP
	end
	setvar $RND_STRING $RND_STRING&$RND_RESULT&" "
	add $RND_IDX 1
end
getrnd $RND_IDX 1 100
getword $RND_STRING $RND_TEMP $RND_IDX
setvar $RND_IDX 1
setvar $RND_TRING ""
while ($RND_IDX <= SECTOR.WARPCOUNT[$CURRENT_SECTOR])
	cuttext $RND_TEMP $RND_PTR $RND_IDX 1
	setvar $RND_TRING $RND_TRING&$RND_SURROUND[$RND_PTR]
	add $RND_IDX 1
end
send "c v 0* y* "&$CURRENT_SECTOR&"* q "&$RND_TRING
if ($SURROUNDAUTOCAPTURE)
	gosub :QUIKSTATS
	if ($STARTINGLOCATION = "Citadel")
		setvar $STARTINGLOCATION "Command"
		gosub :GETSECTORDATA
		gosub :FASTCAPTURE
		setvar $STARTINGLOCATION "Citadel"
	else
		gosub :GETSECTORDATA
		gosub :FASTCAPTURE
	end
end
if (($STARTINGLOCATION = "Citadel") or ($STARTINGLOCATION = "Planet"))
	gosub :LANDINGSUB
end
send "'{" $BOT_NAME "} - Surrounded sector "&$CURRENT_SECTOR&".*"
settextlinetrigger SURROUNDMESSAGE :CONTINUESURROUNDMESSAGE "{"&$BOT_NAME&"} - Surrounded sector "&$CURRENT_SECTOR&"."
pause

:CONTINUESURROUNDMESSAGE
echo "*"&ANSI_14&$SURROUNDOUTPUT&"*"&ANSI_7
goto :WAIT_FOR_COMMAND

:GETSHIPSTATS
send "c;q"
settextlinetrigger GETSHIPOFFENSE :SHIPOFFENSEODDS "Offensive Odds: "
settextlinetrigger GETSHIPFIGHTERS :SHIPMAXFIGSPERATTACK " TransWarp Drive:   "
settextlinetrigger GETSHIPMINES :SHIPMAXMINES " Mine Max:  "
pause

:SHIPOFFENSEODDS
getwordpos CURRENTANSILINE $POS "[0;31m:[1;36m1"
if ($POS > 0)
	gettext CURRENTANSILINE $SHIP_OFFENSIVE_ODDS "Offensive Odds[1;33m:[36m " "[0;31m:[1;36m1"
	striptext $SHIP_OFFENSIVE_ODDS "."
	striptext $SHIP_OFFENSIVE_ODDS " "
	gettext CURRENTANSILINE $SHIP_FIGHTERS_MAX "Max Fighters[1;33m:[36m" "[0;32m Offensive Odds"
	striptext $SHIP_FIGHTERS_MAX ","
	striptext $SHIP_FIGHTERS_MAX " "
end
pause

:SHIPMAXMINES
gettext CURRENTLINE $SHIP_MINES_MAX "Mine Max:" "Beacon Max:"
striptext $SHIP_MINES_MAX " "
pause

:SHIPMAXFIGSPERATTACK
getwordpos CURRENTANSILINE $POS "[0m[32m Max Figs Per Attack[1;33m:[36m"
if ($POS > 0)
	gettext CURRENTANSILINE $SHIP_MAX_ATTACK "[0m[32m Max Figs Per Attack[1;33m:[36m" "[0;32mTransWarp"
	striptext $SHIP_MAX_ATTACK " "
end
killtrigger GETSHIPOFFENCE
killtrigger GETSHIPFIGHTERS
killtrigger GETSHIPMINES
return

:LANDINGSUB
send "l" $PLANET "*z  n  z  n  *  "
savevar $PLANET
setvar $SUCESSFULCITADEL FALSE
setvar $SUCESSFULPLANET FALSE
settextlinetrigger NOPLANET :NOPLANET "There isn't a planet in this sector."
settextlinetrigger NO_LAND :NO_LAND "since it couldn't possibly stand"
settextlinetrigger PLANET :PLANET "Planet #"
settextlinetrigger WRONGONE :WRONG_NUM "That planet is not in this sector."
pause

:NOPLANET
killtrigger NO_LAND
killtrigger PLANET
killtrigger WRONGONE
send "'{" $BOT_NAME "} - No Planet in Sector!*"
return

:NO_LAND
killtrigger NOPLANET
killtrigger PLANET
killtrigger WRONGONE
send "'{" $BOT_NAME "} - This ship cannot land!*"
return

:PLANET
getword CURRENTLINE $PNUM_CK 2
striptext $PNUM_CK "#"
if ($PNUM_CK <> $PLANET)
	killtrigger NO_LAND
	killtrigger WRONGONE
	killtrigger NO_PLANET
	send "q"
	goto :WRONG_NUM
end
killtrigger NOPLANET
killtrigger NO_LAND
killtrigger WRONGONE
settexttrigger WRONG_NUM :WRONG_NUM "That planet is not in this sector."
settexttrigger PLANET :PLANET_PROMPT "Planet command"
pause

:WRONG_NUM
killtrigger PLANET
send "**'{" $BOT_NAME "} - Incorrect Planet Number*"
return

:PLANET_PROMPT
killtrigger WRONG_NUM
setvar $CURRENTBOTPLANET $PLANET
savevar $CURRENTBOTPLANET
send "c"
settexttrigger BUILD_CIT :BUILD_CIT "Do you wish to construct one?"
settexttrigger IN_CIT :IN_CIT "Citadel command"
settexttrigger NOCITALLOWED :BUILD_CIT "Citadels are not allowed in FedSpace."
settexttrigger CITNOTBUILTYET :BUILD_CIT "Be patient, your Citadel is not yet finished."
pause

:BUILD_CIT
killtrigger IN_CIT
killtrigger NOCITALLOWED
killtrigger BUILD_CIT
killtrigger CITNOTBUILTYET
setvar $SUCESSFULPLANET TRUE
send "n*"
setvar $STARTINGLOCATION "Planet"
return

:IN_CIT
killtrigger IN_CIT
killtrigger NOCITALLOWED
killtrigger BUILD_CIT
killtrigger CITNOTBUILTYET
setvar $SUCESSFULCITADEL TRUE
setvar $STARTINGLOCATION "Citadel"
return

:GETSECTORDATA
gosub :KILLTHETRIGGERS
if ($STARTINGLOCATION = "Citadel")
	send "s* "
else
	send "** "
end
setvar $SECTORDATA ""

:SECTORSLINE_CIT_KILL
setvar $LINE CURRENTANSILINE
setvar $LINE $STARTLINE&$LINE&$ENDLINE
setvar $SECTORDATA $SECTORDATA&$LINE
getwordpos $LINE $POS "Warps to Sector(s) "
if ($POS > 0)
	goto :GOTSECTORDATA
else
	settextlinetrigger GETLINE :SECTORSLINE_CIT_KILL
end
pause

:GOTSECTORDATA
getwordpos $SECTORDATA $BEACONPOS "[0m[35mBeacon  [1;33m:"
if ($BEACONPOS > 0)
	setvar $CONTAINSBEACON TRUE
else
	setvar $CONTAINSBEACON FALSE
end
gosub :GETTRADERS
gosub :GETEMPTYSHIPS
gosub :GETFAKETRADERS
return

:GETTRADERS
getwordpos $SECTORDATA $POSTRADER "[0m[33mTraders [1m:"
if ($POSTRADER > 0)
	gettext $SECTORDATA $TRADERDATA "[0m[33mTraders [1m:" "[0m[1;32mWarps to Sector(s) "
	setvar $TRADERDATA $STARTLINE&$TRADERDATA
	gettext $TRADERDATA $TEMP $STARTLINE $ENDLINE
	setvar $REALTRADERCOUNT 0
	setvar $CORPIECOUNT 0
	while ($TEMP <> "")
		getlength $STARTLINE&$TEMP&$ENDLINE $LENGTH
		cuttext $TRADERDATA $TRADERDATA ($LENGTH + 1) 9999
		striptext $TEMP $STARTLINE
		striptext $TEMP $ENDLINE
		striptext $TEMP "[0m          "
		striptext $TEMP "[0m[33mTraders [1m:"
		setvar $J 1
		setvar $ISFOUND FALSE
		while (($J < $RANKSLENGTH) and ($ISFOUND = FALSE))
			getwordpos $TEMP $POS $RANKS[$J]
			if ($POS > 0)
				getlength $RANKS[$J] $LENGTH
				cuttext $TEMP $TEMP ($POS + ($LENGTH + 1)) 9999
				if ($J <= 10)
					setvar $TRADERS[($REALTRADERCOUNT + 1)][2] TRUE
				else
					setvar $TRADERS[($REALTRADERCOUNT + 1)][2] FALSE
				end
				setvar $ISFOUND TRUE
			end
			add $J 1
		end
		getwordpos $TEMP $POS "[0;32m w/"
		getwordpos $TEMP $POS2 "[0;35m[[31mOwned by[35m]"
		if (($POS > 0) and ($POS2 <= 0))
			getwordpos $TEMP $POS "[[1;36m"
			if ($POS > 0)
				gettext $TEMP $TEMPCORP "[[1;36m" "[0;34m]"
				striptext $TEMPCORP ""
			else
				setvar $TEMPCORP 99999
			end
			replacetext $TEMP "[0;34m" "[34m"
			getwordpos $TEMP $POS "[34m"
			cuttext $TEMP $TEMP 1 $POS
			striptext $TEMP ""
			lowercase $TEMP
			setvar $TRADERS[($REALTRADERCOUNT + 1)] $TEMP
			setvar $TRADERS[($REALTRADERCOUNT + 1)][1] $TEMPCORP
			add $REALTRADERCOUNT 1
			if ($TEMPCORP = $CORP)
				add $CORPIECOUNT 1
			end
		end
		gettext $TRADERDATA $TEMP $STARTLINE $ENDLINE
	end
else
	setvar $REALTRADERCOUNT 0
	setvar $CORPIECOUNT 0
end
return

:GETEMPTYSHIPS
getwordpos $SECTORDATA $POSSHIPS "[0m[33mShips   [1m:"
if ($POSSHIPS > 0)
	gettext $SECTORDATA $SHIPDATA "[0m[33mShips   [1m:" "[0m[1;32mWarps to Sector(s) [33m:"
	setvar $SHIPDATA $STARTLINE&$SHIPDATA
	gettext $SHIPDATA $TEMP $STARTLINE $ENDLINE
	setvar $EMPTYSHIPCOUNT 0
	while ($TEMP <> "")
		getlength $STARTLINE&$TEMP&$ENDLINE $LENGTH
		cuttext $SHIPDATA $SHIPDATA ($LENGTH + 1) 9999
		striptext $TEMP $STARTLINE
		striptext $TEMP "  "
		striptext $TEMP $ENDLINE
		getwordpos $TEMP $POS2 "[0;35m[[31mOwned by[35m]"
		if ($POS2 > 0)
			cuttext $TEMP $TEMP $POS2 9999
			striptext $TEMP "[0;35m[[31mOwned by[35m] "
			getwordpos $TEMP $POS3 ",[0;32m w/"
			cuttext $TEMP $TEMP 0 $POS3
			getwordpos $TEMP $POS4 "[34m[[1;36m"
			striptext $TEMP "[1;33m,"
			if ($POS4 > 0)
				cuttext $TEMP $TEMP $POS4 9999
				striptext $TEMP "[34m[[1;36m"
				striptext $TEMP "[0;34m]"
			end
			setvar $EMPTYSHIPS[($EMPTYSHIPCOUNT + 1)] $TEMP
			add $EMPTYSHIPCOUNT 1
		end
		gettext $SHIPDATA $TEMP $STARTLINE $ENDLINE
	end
else
	setvar $EMPTYSHIPCOUNT 0
end
return

:GETFAKETRADERS
setvar $FEDERALSINSECTOR FALSE
getwordpos $SECTORDATA $POSSHIPS "[0m[33mShips   [1m:"
getwordpos $SECTORDATA $POSTRADERS "[0m[33mTraders [1m:"
getwordpos $SECTORDATA $POSFEDERALS "[0m[33mFederals[1m:"
if ($POSFEDERALS > 0)
	setvar $FEDERALSINSECTOR TRUE
end
if ($POSTRADERS > 0)
	gettext $SECTORDATA $FAKEDATA "[1;32mSector  [33m:" "[0m[33mTraders [1m:"
	gosub :GRABFAKEDATA
elseif ($POSSHIPS > 0)
	gettext $SECTORDATA $FAKEDATA "[1;32mSector  [33m:" "[0m[33mShips   [1m:"
	gosub :GRABFAKEDATA
else
	gettext $SECTORDATA $FAKEDATA "[1;32mSector  [33m:" "[0m[1;32mWarps to Sector(s) [33m:"
	gosub :GRABFAKEDATA
end
return

:GRABFAKEDATA
setvar $FAKEDATA $STARTLINE&$FAKEDATA
gettext $FAKEDATA $TEMP $STARTLINE $ENDLINE
setvar $FAKETRADERCOUNT 0
while ($TEMP <> "")
	getlength $STARTLINE&$TEMP&$ENDLINE $LENGTH
	cuttext $FAKEDATA $FAKEDATA ($LENGTH + 1) 9999
	striptext $TEMP $STARTLINE
	striptext $TEMP "  "
	striptext $TEMP $ENDLINE
	getwordpos $TEMP $POS "33m,[0;32m w/ "
	if ($POS <= 0)
		getwordpos $TEMP $POS "[0;32mw/ "
	end
	getwordpos $TEMP $POS2 "[33m, [0;32mwith"
	getwordpos $TEMP $POS3 "[0;35m[[31mOwned by[35m]"
	getwordpos $TEMP $POS4 "[0;32mw/ "&#27&"[1;33m"
	if ((($POS4 > 0) or ($POS > 0) or ($POS2 > 0)) and ($POS3 <= 0))
		setvar $FAKETRADERS[($FAKETRADERCOUNT + 1)] $TEMP
		add $FAKETRADERCOUNT 1
	end
	gettext $FAKEDATA $TEMP $STARTLINE $ENDLINE
end
return

:WAIT_FOR_COMMAND
killalltriggers
setvar $ROUTING ""
setvar $TEMP_BOT_NAME ""
loadvar $BOTISDEAF
if ($BOTISDEAF = TRUE)
	gosub :DONEPREFER
end
setvar $ALIVE_COUNT 0
if ($STARDOCK <= 0)
	setvar $STARDOCK STARDOCK
	savevar $STARDOCK
end
if ($RYLOS <= 0)
	setvar $RYLOS RYLOS
	savevar $RYLOS
end
if ($ALPHA_CENTAURI <= 0)
	setvar $ALPHA_CENTAURI ALPHACENTAURI
	savevar $ALPHA_CENTAURI
end
setvar $SELF_COMMAND FALSE
setvar $SCRUBONLY FALSE
if ($BOTISOFF = TRUE)
	settextlinetrigger ACTIVATE_BOT :CHECK_ROUTING $BOT_NAME&" bot on"
end
if ((CONNECTED <> TRUE) and ($DORELOG = TRUE))
	goto :RELOG_ATTEMPT
end
settextouttrigger USER :USER_ACCESS ">"
settextouttrigger UPARROW :USER_ACCESS #28
settextouttrigger DOWNARROW :USER_ACCESS #29
settextouttrigger UPARROW2 :USER_ACCESS #27&"[A"
settextouttrigger DOWNARROW2 :USER_ACCESS #27&"[B"
settextouttrigger TABKEY :HOTKEY_ACCESS #9
settextouttrigger RIGHTARROW :HOTKEY_ACCESS #27&"[D"
settextouttrigger RIGHTARROW2 :HOTKEY_ACCESS #31
settextouttrigger LEFTARROW :HOTKEY_ACCESS #27&"[C"
settextouttrigger LEFTARROW2 :HOTKEY_ACCESS #30
setvar $AUTHORIZATION 0
setvar $LOGGED 0
seteventtrigger SHUTDOWNTHEMODULE :SHUTDOWN "SCRIPT STOPPED" $LAST_LOADED_MODULE
settextlinetrigger GETSHIPOFFENSIVE :SETSHIPOFFENSIVEODDS "Offensive Odds: "
settextlinetrigger GETSHIPMAXFIGHTERS :SETSHIPMAXFIGATTACK " TransWarp Drive:   "
settextlinetrigger FEDERASE :FEDERASEFIG "The Federation We destroyed your Corp's "
settextlinetrigger FIGHTERSERASE :ERASEFIG " of your fighters in sector "
settextlinetrigger WARPFIGERASE :ERASEWARPFIG "You do not have any fighters in Sector "
settextlinetrigger GETPLANETNUMBER :SETPLANETNUMBER "Planet #"
setdelaytrigger KEEPALIVE :KEEPALIVE 30000
settextlinetrigger OWN_COMMAND :CHECK_ROUTING $BOT_NAME
settextlinetrigger OWN_COMMAND_TEAM :CHECK_ROUTING_TEAM $BOT_TEAM_NAME
if ($BOTISOFF = FALSE)
	settextlinetrigger LOGINMEMO :LOGINMEMO "You have a corporate memo from "
end
if ($DORELOG = TRUE)
	seteventtrigger RELOG :RELOG_ATTEMPT "CONNECTION LOST"
end
settextlinetrigger CLEARBUSTS :ERASEBUSTS ">[Busted:"
settexttrigger ONLINE_WATCH :ONLINE_WATCH "Your session will be terminated in "
pause

:LOGINMEMO
getwordpos CURRENTANSILINE $POS #27&"[32mYou have a corporate memo from "&#27&"[1;36m"
if ($POS > 0)
	gettext CURRENTANSILINE $USER_NAME #27&"[32mYou have a corporate memo from "&#27&"[1;36m" #27&"[0;32m."&#13
	setvar $I 1
	setvar $TEMPUSERNAME $USER_NAME
	lowercase $TEMPUSERNAME
	lowercase $USER_NAME
	while ($I <= $CORPYCOUNT)
		setvar $TEMPCORPY $CORPY[$I]
		lowercase $TEMPCORPY
		if ($TEMPCORPY = $TEMPUSERNAME)
			goto :ENDLOGINMEMO
		end
		add $I 1
	end
	add $CORPYCOUNT 1
	setvar $CORPY[$CORPYCOUNT] $USER_NAME
	cuttext $USER_NAME $CUT_USER_NAME 1 6
	striptext $CUT_USER_NAME " "
	setvar $LOGGEDIN[$CUT_USER_NAME] 1
	send "'{" $BOT_NAME "} - User Verified - " $USER_NAME "*"
end

:ENDLOGINMEMO
settextlinetrigger LOGINMEMO :LOGINMEMO "You have a corporate memo from "
pause

:CHECK_ROUTING_TEAM
setvar $TEMP_BOT_NAME $BOT_TEAM_NAME
goto :DO_ROUTING

:CHECK_ROUTING_ALL
setvar $TEMP_BOT_NAME "misanthrope"
goto :DO_ROUTING

:CHECK_ROUTING
setvar $TEMP_BOT_NAME $BOT_NAME

:DO_ROUTING
setvar $CURRENTLINE CURRENTLINE
setvar $CURRENTANSILINE CURRENTANSILINE
gosub :KILLTHETRIGGERS
getword CURRENTLINE $ROUTING 1
if (($ROUTING = "'"&$TEMP_BOT_NAME) and ($TEMP_BOT_NAME <> "misanthrope"))
	goto :OWN_COMMAND
elseif (($ROUTING = "R") and ($BOTISOFF <> TRUE))
	goto :COMMAND
elseif (($ROUTING = "P") and ($BOTISOFF <> TRUE))
	goto :PAGE_COMMAND
else
	goto :WAIT_FOR_COMMAND
end

:OWN_COMMAND
cuttext $CURRENTANSILINE $ANSI_CK1 1 1
if ($ANSI_CK1 <> "")
	goto :WAIT_FOR_COMMAND
end
getword $CURRENTLINE $RADIO_TYPE 1
striptext $RADIO_TYPE $TEMP_BOT_NAME
setvar $USER_COMMAND_LINE $CURRENTLINE
setvar $USER_COMMAND_LINE $USER_COMMAND_LINE&"              "
lowercase $USER_COMMAND_LINE
if ($RADIO_TYPE = "'")
	getlength "'"&$TEMP_BOT_NAME&" " $LENGTH
	cuttext $USER_COMMAND_LINE $USER_COMMAND_LINE ($LENGTH + 1) 9999
	setvar $USER_SEC_LEVEL 9
	getword $CURRENTLINE $COMMAND 2
	getwordpos $COMMAND $POS "'"
	getwordpos $COMMAND $POS2 "`"
	if (($POS = 1) or ($POS2 = 1))
		goto :WAIT_FOR_COMMAND
	end
	getlength $COMMAND&" " $COMMANDLENGTH
	cuttext $USER_COMMAND_LINE $USER_COMMAND_LINE ($COMMANDLENGTH + 1) 9999
	gosub :GETPARAMETERS
	goto :COMMAND_PROCESSING
else
	goto :WAIT_FOR_COMMAND
end

:COMMAND
setvar $ANSI_LINE $CURRENTANSILINE
getwordpos $ANSI_LINE $POS "[36mR"
if ($POS = 0)
	goto :WAIT_FOR_COMMAND
end
cuttext $CURRENTLINE $USER_NAME 3 6
striptext $USER_NAME " "
cuttext $CURRENTLINE $USER_COMMAND_LINE 10 999
getword $USER_COMMAND_LINE $BOTNAME_CHK 1
if ($BOTNAME_CHK <> $TEMP_BOT_NAME)
	goto :WAIT_FOR_COMMAND
end
getlength $TEMP_BOT_NAME&" " $LENGTH
cuttext $USER_COMMAND_LINE&"          " $USER_COMMAND_LINE ($LENGTH + 1) 9999
lowercase $USER_COMMAND_LINE
setvar $USER_COMMAND_LINE $USER_COMMAND_LINE&"              "
getword $USER_COMMAND_LINE $COMMAND 1
if (($COMMAND = "bot") or ($COMMAND = "relog"))
	goto :WAIT_FOR_COMMAND
end
getlength $COMMAND $LENGTH
cuttext $USER_COMMAND_LINE&"          " $USER_COMMAND_LINE ($LENGTH + 1) 9999
gosub :GETPARAMETERS
gosub :VERIFY_USER_STATUS
if ($AUTHORIZATION = 0)
	send "'{" $BOT_NAME "} - Send a corporate memo to login.*"
	goto :WAIT_FOR_COMMAND
end
goto :COMMAND_PROCESSING

:PAGE_COMMAND
cuttext $CURRENTLINE $USER_NAME 3 6
striptext $USER_NAME " "
cuttext $CURRENTLINE $USER_COMMAND_LINE 9 999
striptext $USER_COMMAND_LINE " "
getwordpos $USER_COMMAND_LINE $POS $BOT_NAME&":"&$BOT_PASSWORD&":"&$SUBSPACE
if ($POS > 0)
	add $CORPYCOUNT 1
	setvar $CORPY[$CORPYCOUNT] $USER_NAME
	setvar $LOGGEDIN[$USER_NAME] 1
	send "'{" $BOT_NAME "} - User Verified - " $USER_NAME "*"
else
	getword $USER_COMMAND_LINE $BOTNAME_CHK 1
	if ($BOTNAME_CHK <> $TEMP_BOT_NAME)
		goto :WAIT_FOR_COMMAND
	end
	getlength $TEMP_BOT_NAME&" " $LENGTH
	cuttext $USER_COMMAND_LINE&"          " $USER_COMMAND_LINE ($LENGTH + 1) 9999
	lowercase $USER_COMMAND_LINE
	setvar $USER_COMMAND_LINE $USER_COMMAND_LINE&"              "
	getword $USER_COMMAND_LINE $COMMAND 1
	if (($COMMAND = "bot") or ($COMMAND = "relog"))
		goto :WAIT_FOR_COMMAND
	end
	getlength $COMMAND $LENGTH
	cuttext $USER_COMMAND_LINE&"          " $USER_COMMAND_LINE ($LENGTH + 1) 9999
	gosub :GETPARAMETERS
	gosub :VERIFY_USER_STATUS
	if ($AUTHORIZATION = 0)
		send "'{" $BOT_NAME "} - Send a corporate memo to login.*"
		goto :WAIT_FOR_COMMAND
	end
	goto :COMMAND_PROCESSING
end
goto :WAIT_FOR_COMMAND

:USER_ACCESS
gosub :BIGDELAY_KILLTHETRIGGERS
echo "**"
gosub :SELFCOMMANDPROMPT
lowercase $USER_COMMAND_LINE
if ($USER_COMMAND_LINE = "")
	echo CURRENTANSILINE
	goto :WAIT_FOR_COMMAND
elseif ($USER_COMMAND_LINE = "?")
	goto :ECHO_HELP
elseif ($USER_COMMAND_LINE = "help")
	goto :ECHO_HELP
end

:RUNUSERCOMMANDLINE
setvar $SELF_COMMAND TRUE
setvar $USER_COMMAND_LINE $USER_COMMAND_LINE&"              "
setvar $AUTHORIZATION 9
setvar $USER_SEC_LEVEL 9
getword $USER_COMMAND_LINE $COMMAND 1
getlength $COMMAND&" " $COMMANDLENGTH
getwordpos $COMMAND $POS "'"
getwordpos $COMMAND $POS2 "`"
if (($POS <> 1) and ($POS2 <> 1))
	cuttext $USER_COMMAND_LINE $USER_COMMAND_LINE ($COMMANDLENGTH + 1) 9999
end
gosub :GETPARAMETERS
goto :COMMAND_PROCESSING

:GETPARAMETERS
setvar $I 1
while ($I <= 8)
	getword $USER_COMMAND_LINE&" " $PARMS[$I] $I 0
	add $I 1
end
return

:SELFCOMMANDPROMPT
gosub :BIGDELAY_KILLTHETRIGGERS
setvar $PROMPT ANSI_10&#27&"[255D"&#27&"[255B"&#27&"[K"&ANSI_4&"{"&ANSI_14&$MODE&ANSI_4&"}"&ANSI_15&" "&$BOT_NAME&ANSI_2&">"&ANSI_7
echo $PROMPT

:GETINPUT
killtrigger TEXT
killtrigger REECHO
settextouttrigger TEXT :GETCHARACTER
setdelaytrigger KEEPALIVE :KEEPALIVE 30000
settexttrigger REECHO :REECHO
pause

:GETCHARACTER
getouttext $CHARACTER
if (($CHARACTER = ">") and ($CHARCOUNT <= 0))

:GRIDPROMPT
	gosub :BIGDELAY_KILLTHETRIGGERS
	gosub :CURRENT_PROMPT
	setvar $DOHOLO FALSE
	setvar $DODENS FALSE
	echo ANSI_10&#27&"[255D"&#27&"[255B"&#27&"[K"
	gosub :DISPLAYADJACENTGRIDANSI
	setvar $GRIDPROMPT ANSI_10&#27&"[255D"&#27&"[255B"&#27&"[K"&ANSI_4&"{"&ANSI_14&"Grid Menu - ["&ANSI_15&"H"&ANSI_14&"]olo ["&ANSI_15&"D"&ANSI_14&"]ens "
	if ($CURRENT_PROMPT = "Citadel")
		setvar $GRIDPROMPT $GRIDPROMPT&"["&ANSI_15&"+"&ANSI_14&"]["&ANSI_15&$PGRID_TYPE&ANSI_14&"] ["&ANSI_15&1&ANSI_14&"-"&ANSI_15&$GRIDWARPCOUNT&ANSI_14&"]"&ANSI_4&"}"&ANSI_14&ANSI_2&">"&ANSI_7&" "
	elseif ($CURRENT_PROMPT = "Command")
		setvar $GRIDPROMPT $GRIDPROMPT&"["&ANSI_15&1&ANSI_14&"-"&ANSI_15&$GRIDWARPCOUNT&ANSI_14&"]"&ANSI_4&"}"&ANSI_14&" Move"&ANSI_4&"}"&ANSI_2&">"&ANSI_7
	else
		echo ANSI_12&"*Wrong prompt for Grid Menu*"
		goto :DONEGRIDDINGPROMPT
	end
	echo $GRIDPROMPT
	gosub :BIGDELAY_KILLTHETRIGGERS
	settexttrigger REECHOGRIDMENU :REECHOGRIDMENU
	settextouttrigger TEXT0 :GRIDPROMPT "?"
	settextouttrigger TEXT12 :NEXTMENU ">"
	setdelaytrigger KEEPALIVE :KEEPALIVE 30000
	setvar $I 1
	while ($I <= $GRIDWARPCOUNT)
		settextouttrigger "grid_map"&$I :VISITSECTORPGRID $I
		add $I 1
	end
	settextouttrigger TEXT7 :HOLOGRID #83
	settextouttrigger TEXT8 :HOLOGRID #115
	settextouttrigger TEXT13 :HOLOGRID "h"
	settextouttrigger TEXT14 :HOLOGRID "H"
	settextouttrigger TEXT9 :DENSGRID #68
	settextouttrigger TEXT10 :DENSGRID #100
	if ($CURRENT_PROMPT = "Citadel")
		settextouttrigger TEXT15 :CHANGEPGRIDTYPE "+"
	end
	settextouttrigger TEXT11 :DONEGRIDDINGPROMPT
	pause

	:HOLOGRID
	setvar $DOHOLO TRUE
	goto :DOGRIDSCAN

	:DENSGRID
	setvar $DODENS TRUE

	:DOGRIDSCAN
	gosub :BIGDELAY_KILLTHETRIGGERS
	if ($CURRENT_PROMPT = "Citadel")
		setvar $SCANTEXT "q q z n "
	else
		setvar $SCANTEXT ""
	end
	if ($DOHOLO = TRUE)
		setvar $SCANTEXT $SCANTEXT&"s hzn* "
	elseif ($DODENS = TRUE)
		setvar $SCANTEXT $SCANTEXT&"s dz* "
	end
	if ($CURRENT_PROMPT = "Citadel")
		setvar $SCANTEXT $SCANTEXT&"l "&$PLANET&"*  c  "
	end
	send $SCANTEXT
	if ($CURRENT_PROMPT = "Citadel")
		waiton "<Enter Citadel>"
	else
		waiton "["&CURRENTSECTOR&"]"
	end
	goto :GRIDPROMPT

	:CHANGEPGRIDTYPE
	if ($PGRID_TYPE = "Normal")
		if ($SAFE_SHIP <= 0)
			setvar $PGRID_TYPE "Xport (Not Available)"
			setvar $PGRID_END_COMMAND " scan "
		else
			setvar $PGRID_TYPE "Xport"
			setvar $PGRID_END_COMMAND " x:"&$SAFE_SHIP&" scan "
		end
	elseif (($PGRID_TYPE = "Xport") or ($PGRID_TYPE = "Xport (Not Available)"))
		setvar $PGRID_TYPE "Retreat"
		setvar $PGRID_END_COMMAND " r scan "
	else
		setvar $PGRID_TYPE "Normal"
		setvar $PGRID_END_COMMAND " scan "
	end
	goto :GRIDPROMPT

	:VISITSECTORPGRID
	getouttext $SECTOR
	gosub :BIGDELAY_KILLTHETRIGGERS
	if (SECTOR.WARPS[CURRENTSECTOR][$SECTOR] > 0)
		if ($CURRENT_PROMPT = "Citadel")
			setvar $USER_COMMAND_LINE "pgrid "&SECTOR.WARPS[CURRENTSECTOR][$SECTOR]&" "&$PGRID_END_COMMAND
			goto :RUNUSERCOMMANDLINE
		elseif ($CURRENT_PROMPT = "Command")
			setvar $MOVEINTOSECTOR SECTOR.WARPS[CURRENTSECTOR][$SECTOR]
			gosub :MOVEINTOSECTOR
		end
	end
	goto :DONEGRIDDINGPROMPT

	:DONEGRIDDINGPROMPT
	echo #27&"[255D"&#27&"[255B"&#27&"[K"
	goto :WAIT_FOR_COMMAND

	:NEXTMENU
	settextouttrigger TEXT12 :NEXTMENU ">"
	pause

	:REECHOGRIDMENU
	echo ANSI_10&#27&"[255D"&#27&"[255B"&#27&"[K"&$GRIDPROMPT
	settexttrigger REECHOGRIDMENU :REECHOGRIDMENU
	pause
end
if ($CHARACTER = #13)
	echo #27&"[255D"&#27&"[255B"&#27&"[K"
	setvar $USER_COMMAND_LINE $PROMPTOUTPUT
	gosub :DOADDHISTORY
	goto :DONESELFCOMMANDPROMPT
else
	getlength $CHARACTER $CHARACTERLENGTH
	if ($CHARACTER = #8)
		if ($CHARCOUNT <= 0)
			setvar $CHARCOUNT 0
			setvar $CHARPOS 0
		else
			if ($CHARPOS >= $CHARCOUNT)
				setvar $FRONTMACRO $PROMPTOUTPUT
				setvar $TAILMACRO ""
			else
				cuttext $PROMPTOUTPUT $TAILMACRO ($CHARPOS + 1) 9999
				cuttext $PROMPTOUTPUT $FRONTMACRO 1 $CHARPOS
			end
			getlength $FRONTMACRO $FRONTLENGTH
			if ($FRONTLENGTH > 1)
				cuttext $FRONTMACRO $FRONTMACRO 1 ($FRONTLENGTH - 1)
			else
				setvar $FRONTMACRO ""
			end
			setvar $PROMPTOUTPUT $FRONTMACRO&$TAILMACRO
			getlength $PROMPTOUTPUT $CHARCOUNT
			subtract $CHARPOS 1
			if ($CHARPOS <= 0)
				setvar $CHARPOS 0
			end
			if (($CHARCOUNT - $CHARPOS) > 0)
				echo $PROMPT $PROMPTOUTPUT #27 "[" ($CHARCOUNT - $CHARPOS) "D"
			else
				echo $PROMPT $PROMPTOUTPUT
			end
		end
	elseif (($CHARACTER = #27&"[A") or ($CHARACTER = #28))
		if ($HISTORYCOUNT > 0)
			if ($HISTORYINDEX <= 0)
				setvar $CURRENTPROMPTTEXT $PROMPTOUTPUT
			end
			add $HISTORYINDEX 1
			if ($HISTORYINDEX > $HISTORYMAX)
				setvar $HISTORYINDEX $HISTORYMAX
			elseif ($HISTORYINDEX > $HISTORYCOUNT)
				setvar $HISTORYINDEX $HISTORYCOUNT
			end
			getlength $HISTORY[$HISTORYINDEX] $CHARCOUNT
			setvar $CHARPOS $CHARCOUNT
			echo $PROMPT $HISTORY[$HISTORYINDEX]
			setvar $PROMPTOUTPUT $HISTORY[$HISTORYINDEX]
		end
	elseif (($CHARACTER = #27&"[B") or ($CHARACTER = #29))
		if ($HISTORYCOUNT > 0)
			if ($HISTORYINDEX <= 0)
				setvar $CURRENTPROMPTTEXT $PROMPTOUTPUT
			end
			subtract $HISTORYINDEX 1
			if ($HISTORYINDEX < 1)
				setvar $HISTORYINDEX 0
				getlength $CURRENTPROMPTTEXT $CHARCOUNT
				setvar $CHARPOS $CHARCOUNT
				echo $PROMPT $CURRENTPROMPTTEXT
				setvar $PROMPTOUTPUT $CURRENTPROMPTTEXT
			else
				getlength $HISTORY[$HISTORYINDEX] $CHARCOUNT
				setvar $CHARPOS $CHARCOUNT
				echo $PROMPT $HISTORY[$HISTORYINDEX]
				setvar $PROMPTOUTPUT $HISTORY[$HISTORYINDEX]
			end
		end
	elseif (($CHARACTER = #27&"[D") or ($CHARACTER = #31))
		if ($CHARPOS > 0)
			subtract $CHARPOS 1
			echo ANSI_10 $CHARACTER
		end
	elseif ($CHARCOUNT > 80)
	elseif (($CHARACTER = #27&"[C") or ($CHARACTER = #30))
		if ($CHARPOS <= $CHARCOUNT)
			add $CHARPOS 1
			echo ANSI_10 $CHARACTER
		end
	elseif (($CHARACTERLENGTH > 1) or ($CHARACTERLENGTH <= 0))
	else

		:TREATASUSUAL
		if ($CHARPOS >= $CHARCOUNT)
			if ($CHARPOS = 1)
				setvar $FRONTMACRO $PROMPTOUTPUT
				setvar $TAILMACRO $CHARACTER
			else
				setvar $FRONTMACRO $PROMPTOUTPUT
				setvar $TAILMACRO $CHARACTER
			end
		else
			cuttext $PROMPTOUTPUT $FRONTMACRO 1 $CHARPOS
			cuttext $PROMPTOUTPUT $TAILMACRO ($CHARPOS + 1) ($CHARCOUNT - ($CHARPOS - 1))
			setvar $FRONTMACRO $FRONTMACRO&$CHARACTER
		end
		setvar $PROMPTOUTPUT $FRONTMACRO&$TAILMACRO
		getlength $PROMPTOUTPUT $CHARCOUNT
		add $CHARPOS 1
		if (($CHARCOUNT - $CHARPOS) > 0)
			echo $PROMPT $PROMPTOUTPUT #27 "[" ($CHARCOUNT - ($CHARPOS + 1)) "D"
		else
			echo $PROMPT $PROMPTOUTPUT
		end
	end
end
settextouttrigger TEXT :GETCHARACTER
pause

:REECHO
if (($CHARCOUNT - $CHARPOS) > 0)
	echo $PROMPT&$PROMPTOUTPUT&#27&"["&($CHARCOUNT - ($CHARPOS + 1))&"D"
else
	echo $PROMPT&$PROMPTOUTPUT
end
settexttrigger REECHO :REECHO
pause

:DONESELFCOMMANDPROMPT
killtrigger TEXT
killtrigger REECHO
return

:DOADDHISTORY
setvar $CHARCOUNT 0
setvar $CURRENTPROMPTTEXT ""
setvar $HISTORYINDEX 0
setvar $CHARPOS 0
setvar $PROMPTOUTPUT ""
setvar $HISTORYSTRING ""
if ($USER_COMMAND_LINE <> "")
	add $HISTORYCOUNT 1
	if ($HISTORYCOUNT > 1)
		setvar $I $HISTORYMAX
		while ($I > 1)
			setvar $HISTORY[$I] $HISTORY[($I - 1)]
			setvar $HISTORYSTRING $HISTORY[$I]&"<<|HS|>>"&$HISTORYSTRING
			subtract $I 1
		end
	end
	setvar $HISTORY[1] $USER_COMMAND_LINE
	setvar $HISTORYSTRING $HISTORY[1]&"<<|HS|>>"&$HISTORYSTRING
	savevar $HISTORYSTRING
end
return

:COMMAND_PROCESSING
lowercase $COMMAND

:COMMAND_FILTERING
cuttext $USER_COMMAND_LINE&"  " $CHECKFORCHAT 1 1
cuttext $COMMAND&"  " $CHECKFORFINDER 1 1
if ($CHECKFORCHAT = "'")
	goto :SS
elseif ($CHECKFORCHAT = "`")
	goto :FED
end
if ($CHECKFORFINDER = "f")
	setvar $TEMP $COMMAND
	striptext $TEMP "f"
	isnumber $TEST $TEMP
	if ($TEST)
		if ($TEMP > 0)
			if ($TEMP = $CORP)
				setvar $COMMAND "f"
			else
				setvar $TARGET_CORP $TEMP
				setvar $COMMAND "owner"
				getword $USER_COMMAND_LINE $PARM1 1
				goto :FINDER
			end
		end
	end
end
if ($COMMAND = "?")
	setvar $COMMAND "help"
end
setvar $I 1
while ($I <= $PARMS)
	if ($PARMS[$I] = "s")
		setvar $PARMS[$I] $STARDOCK
	elseif ($PARMS[$I] = "r")
		setvar $PARMS[$I] $RYLOS
	elseif ($PARMS[$I] = "a")
		setvar $PARMS[$I] $ALPHA_CENTAURI
	elseif ($PARMS[$I] = "h")
		setvar $PARMS[$I] $HOME_SECTOR
	elseif ($PARMS[$I] = "b")
		setvar $PARMS[$I] $BACKDOOR
	elseif ($PARMS[$I] = "x")
		setvar $PARMS[$I] $SAFE_SHIP
	end
	add $I 1
end
setvar $PARM1 $PARMS[1]
setvar $PARM2 $PARMS[2]
setvar $PARM3 $PARMS[3]
setvar $PARM4 $PARMS[4]
setvar $PARM5 $PARMS[5]
setvar $PARM6 $PARMS[6]
setvar $PARM7 $PARMS[7]
setvar $PARM8 $PARMS[8]
if ($COMMAND = "authorize")
	if ($PARM1 <> 0)
		if ($PARM1 = "list")
			gosub :LIST_AUTHORIZED
		elseif ($PARM1 = "help")
			gosub :HELP_AUTHORIZE
		else
			gosub :AUTHORIZE
		end
	else
		setvar $MESSAGE "Please specify a user to authorize or [list] to list authorized users.*"
	end
	gosub :SWITCHBOARD
	goto :WAIT_FOR_COMMAND
end
if ($COMMAND = "deauthorize")
	if ($PARM1 <> 0)
		if ($PARM1 = "help")
			gosub :HELP_DEAUTHORIZE
		else
			gosub :DEAUTHORIZE
		end
	else
		setvar $MESSAGE "Please specify a user to deauthorize.*"
	end
	gosub :SWITCHBOARD
	goto :WAIT_FOR_COMMAND
end
if ($COMMAND = "help")
	if ($PARM1 <> 0)
		lowercase $PARM1
		setvar $I 1
		while ($I <= 7)
			setvar $TEMPTYPE $TYPES[$I]
			lowercase $TEMPTYPE
			if ($PARM1 = $TEMPTYPE)
				setvar $CURRENTLIST $INTERNALCOMMANDLISTS[$I]
				goto :COMMAND_LIST
			end
			add $I 1
		end
		fileexists $DOESEXIST "scripts\MomBot\Help\"&$PARM1&".txt"
		if ($DOESEXIST)
			readtoarray "scripts\MomBot\Help\"&$PARM1&".txt" $HELP_ARRAY
			setvar $I 1
			setvar $HELPOUTPUT ""
			while ($I <= $HELP_ARRAY)
				striptext $HELP_ARRAY[$I] #13
				striptext $HELP_ARRAY[$I] "`"
				striptext $HELP_ARRAY[$I] "'"
				replacetext $HELP_ARRAY[$I] "=" "-"
				setvar $TEMP $HELP_ARRAY[$I]
				getlength $TEMP $LENGTH
				setvar $ISTOOLONG FALSE
				while ($LENGTH > 70)
					setvar $ISTOOLONG TRUE
					cuttext $TEMP $TEMP 71 ($LENGTH - 70)
					getlength $TEMP $LENGTH
				end
				setvar $HELPOUTPUT $HELPOUTPUT&$HELP_ARRAY[$I]
				if ($LENGTH <= 1)
					setvar $HELPOUTPUT $HELPOUTPUT&"  "
				end
				setvar $HELPOUTPUT $HELPOUTPUT&"*"
				add $I 1
			end
			setvar $HELPOUTPUT $HELPOUTPUT&"*"
			setvar $MESSAGE $HELPOUTPUT
			if ($SELF_COMMAND)
				gosub :SWITCHBOARD
			else
				send "'*{"&$BOT_NAME&"} - *"&$HELPOUTPUT&"*"
			end
		else
			setvar $MESSAGE "No help file available for "&$PARM1&".*"
			gosub :SWITCHBOARD
		end
		goto :WAIT_FOR_COMMAND
	else
		if ($SELF_COMMAND)
			goto :ECHO_HELP
		else
			goto :SS_HELP
		end
	end
end
if ($COMMAND = 0)
	send "'{" $BOT_NAME "} - You are logged into this bot.  Use "&$BOT_NAME&" help for commands.*"
	goto :WAIT_FOR_COMMAND
end
getwordpos " "&$USER_COMMAND_LINE&" " $STOPCHECK " off "
gosub :FORMATCOMMAND
gosub :FINDCOMMAND
if ($CURRENTCATEGORY = "Modes")
	if ($STOPCHECK > 0)
		killtrigger SHUTDOWNTHEMODULE
		stop $LAST_LOADED_MODULE
		setvar $MODE "General"
		send "'{" $BOT_NAME "} - "&$FORMATTED_COMMAND&" mode is now off.*"
		goto :WAIT_FOR_COMMAND
	end
end
if (($DOESEXIST > 0) or ($DOESEXISTHIDDEN > 0))
	goto :RUN_MODULE
else
	getwordpos $INTERNALCOMMANDLIST&$DOUBLEDCOMMANDLIST $POS " "&$COMMAND&" "
	if ($POS > 0)
		gosub :KILLTHETRIGGERS
		goto ":"&$COMMAND
	end
end
setvar $MESSAGE $FORMATTED_COMMAND&" is not a valid command.*"
gosub :SWITCHBOARD
goto :WAIT_FOR_COMMAND

:FORMATCOMMAND
cuttext $COMMAND&" " $FIRSTCHAR 1 1
cuttext $COMMAND&" " $RESTOFCOMMAND 2 999
uppercase $FIRSTCHAR
setvar $FORMATTED_COMMAND $FIRSTCHAR&$RESTOFCOMMAND
striptext $FORMATTED_COMMAND " "
return

:FINDCOMMAND
setvar $MODULECATEGORY ""
setvar $I 1
while ($I <= 3)
	setvar $J 1
	while ($J <= 7)
		if ($I = 3)
			fileexists $DOESEXIST "scripts\MomBot\"&$CATAGORIES[$I]&"\"&$COMMAND&".cts"
			fileexists $DOESEXISTHIDDEN "scripts\MomBot\"&$CATAGORIES[$I]&"\_"&$COMMAND&".cts"
			if ($DOESEXIST or $DOESEXISTHIDDEN)
				setvar $CURRENTCATEGORY $CATAGORIES[$I]
				if ($DOESEXISTHIDDEN)
					setvar $MODULECATEGORY $CATAGORIES[$I]&"\_"
				else
					setvar $MODULECATEGORY $CATAGORIES[$I]&"\"
				end
				setvar $CURRENTLIST $INTERNALCOMMANDLIST[$J]
				return
			end
		else
			fileexists $DOESEXIST "scripts\MomBot\"&$CATAGORIES[$I]&"\"&$TYPES[$J]&"\"&$COMMAND&".cts"
			fileexists $DOESEXISTHIDDEN "scripts\MomBot\"&$CATAGORIES[$I]&"\"&$TYPES[$J]&"\_"&$COMMAND&".cts"
			if ($DOESEXIST or $DOESEXISTHIDDEN)
				setvar $CURRENTCATEGORY $CATAGORIES[$I]
				if ($DOESEXISTHIDDEN)
					setvar $MODULECATEGORY $CATAGORIES[$I]&"\"&$TYPES[$J]&"\_"
				else
					setvar $MODULECATEGORY $CATAGORIES[$I]&"\"&$TYPES[$J]&"\"
				end
				setvar $CURRENTLIST $INTERNALCOMMANDLIST[$J]
				return
			end
		end
		add $J 1
	end
	add $I 1
end
return

:RUN_MODULE
gosub :KILLTHETRIGGERS
savevar $COMMAND
savevar $USER_COMMAND_LINE
savevar $PARM1
savevar $PARM2
savevar $PARM3
savevar $PARM4
savevar $PARM5
savevar $PARM6
savevar $PARM7
savevar $PARM8
savevar $BOT_NAME
savevar $UNLIMITEDGAME
savevar $CAP_FILE
savevar $BOT_TURN_LIMIT
savevar $PASSWORD
savevar $MODE
savevar $MBBS
savevar $WARN
savevar $PTRADESETTING
savevar $RYLOS
savevar $ALPHA_CENTAURI
savevar $STARDOCK
savevar $BACKDOOR
savevar $HOME_SECTOR
savevar $PORT_MAX
savevar $STEAL_FACTOR
savevar $ROB_FACTOR
savevar $SUBSPACE
savevar $MULTIPLE_PHOTONS
if ($CURRENTCATEGORY = "Modes")
	stop $LAST_LOADED_MODULE
	setvar $LAST_LOADED_MODULE "scripts\MomBot\"&$MODULECATEGORY&$COMMAND&".cts"
	setvar $MODE $FORMATTED_COMMAND
end
stop "scripts\MomBot\"&$MODULECATEGORY&$COMMAND&".cts"
stop "scripts\MomBot\"&$MODULECATEGORY&$COMMAND&".cts"
stop "scripts\MomBot\"&$MODULECATEGORY&$COMMAND&".cts"
stop "scripts\MomBot\"&$MODULECATEGORY&$COMMAND&".cts"
stop "scripts\MomBot\"&$MODULECATEGORY&$COMMAND&".cts"
load "scripts\MomBot\"&$MODULECATEGORY&$COMMAND&".cts"
goto :WAIT_FOR_COMMAND

:HOTKEY_ACCESS
gosub :BIGDELAY_KILLTHETRIGGERS
setvar $SELF_COMMAND TRUE
setvar $COMMAND ""
setvar $INVALID FALSE
echo #27 "[1A" #27 "[K" ANSI_15 "**Hotkey" ANSI_4
getconsoleinput $TEMPCHARACTER SINGLEKEY

:CHECKHOTKEY
getcharcode $TEMPCHARACTER $CHARCODE
gosub :KILLTHETRIGGERS
setvar $TEMP $HOTKEYS[$CHARCODE]
if (($TEMP <> 0) and ($TEMP <> ""))
	setvar $COMMAND $CUSTOM_COMMANDS[$TEMP]
else
	setvar $INVALID TRUE
end
cuttext $COMMAND&"  " $TEST 1 1
if ($CHARCODE = 48)
	setvar $I 10
	goto :RUNHOTSCRIPT
elseif ($CHARCODE = 63)
	goto :ECHO_HELP
elseif (($CHARCODE >= 49) and ($CHARCODE <= 57))
	setvar $I ($CHARCODE - 48)
	goto :RUNHOTSCRIPT
elseif (($TEST = ":") and ($INVALID = FALSE))
	goto $COMMAND
elseif ($INVALID = FALSE)
	setvar $USER_COMMAND_LINE $COMMAND
	goto :RUNUSERCOMMANDLINE
end
echo #27 "[10D          " #27 "[10D"
goto :WAIT_FOR_COMMAND

:STOPALL
gosub :KILLTHETRIGGERS
openmenu TWX_STOPALLFAST FALSE
setvar $MODE "General"
send "'{" $BOT_NAME "} - All non-system scripts and modules killed, and modes reset.*"
goto :WAIT_FOR_COMMAND

:LISTALL
listactivescripts $SCRIPTS
setvar $A 1
setvar $MESSAGE " Current script(s) loaded*"
setvar $MESSAGE $MESSAGE&"--------------------------*"
while ($A <= $SCRIPTS)
	setvar $MESSAGE $MESSAGE&"   "&$SCRIPTS[$A]&"*"
	add $A 1
end
if ($SELF_COMMAND <> TRUE)
	setvar $SELF_COMMAND 2
end
gosub :SWITCHBOARD
goto :WAIT_FOR_COMMAND

:STOPMODULES
stop $LAST_LOADED_MODULE
echo ANSI_14 "*<<" ANSI_15 "General Mode Reset" ANSI_14 ">>*" ANSI_7
setvar $MODE "General"
setvar $LAST_LOADED_MODULE ""
goto :WAIT_FOR_COMMAND

:SCRIPT_ACCESS
gosub :KILLTHETRIGGERS
setvar $I 1
echo #27 "[3A" #27 "[K*" #27 "[K*" #27 "[K*" ANSI_14 "*Which script to run?                      *----------------------------------"
while (($I <= $HOTKEY_SCRIPTS) and ($I <= 10))
	if ($HOTKEY_SCRIPTS[$I] <> 0)
		if ($I >= 10)
			settextouttrigger "key"&$I :TRIGGERHOTSCRIPT 0
			echo "*"&ANSI_15&0&ANSI_14&") "&ANSI_15&$HOTKEY_SCRIPTS[$I][1]
		else
			settextouttrigger "key"&$I :TRIGGERHOTSCRIPT $I
			echo "*"&ANSI_15&$I&ANSI_14&") "&ANSI_15&$HOTKEY_SCRIPTS[$I][1]
		end
	end
	add $I 1
end
settextouttrigger ECHOHELP2 :SCRIPT_ACCESS #63
setdelaytrigger NOTFASTENOUGH2 :DONESCRIPTS 9000
settextouttrigger NONEAVAIL2 :DONESCRIPTS
echo #27 "[1A" #27 "[K" ANSI_14 "***Scripts" ANSI_15 ">" ANSI_7
pause

:DONESCRIPTS
echo #27 "[10D          " #27 "[10D"
goto :WAIT_FOR_COMMAND

:TRIGGERHOTSCRIPT
getouttext $I
if ($I = 0)
	setvar $I 10
end

:RUNHOTSCRIPT
gosub :KILLTHETRIGGERS
fileexists $CHK $HOTKEY_SCRIPTS[$I]
if ($CHK)
	load $HOTKEY_SCRIPTS[$I]
else
	echo ANSI_4&"*"&$HOTKEY_SCRIPTS[$I]&" does not exist in specified location.  Please check your "&$SCRIPT_FILE&" file to make sure it is correct.*"&ANSI_7
end
goto :WAIT_FOR_COMMAND

:MOWSWITCH
getinput $PARM1 "Mow To:"
getword $PARM1 $PARM1 1
striptext $PARM1 " "
if (($PARM1 = 0) or ($PARM1 = ""))
	goto :WAIT_FOR_COMMAND
end
setvar $USER_COMMAND_LINE "mow "&$PARM1&" 1"
goto :RUNUSERCOMMANDLINE

:FOTONSWITCH
if ($MODE = "Foton")
	setvar $USER_COMMAND_LINE "foton off"
	goto :RUNUSERCOMMANDLINE
else
	setvar $USER_COMMAND_LINE "foton on p"
	goto :RUNUSERCOMMANDLINE
end
goto :WAIT_FOR_COMMAND

:ADD_GAME
getinput $NEW_BOT_NAME ANSI_13&"What is the 'in game' name of the bot? (one word, no spaces)"&ANSI_7
striptext $NEW_BOT_NAME "^"
striptext $NEW_BOT_NAME " "
lowercase $NEW_BOT_NAME
if ($NEW_BOT_NAME = "")
	goto :ADD_GAME
end
if (PASSWORD = "")
	getinput $PASSWORD "Please Enter your Game password"
end
delete $GCONFIG_FILE
write $GCONFIG_FILE $NEW_BOT_NAME
setvar $BOT_NAME $NEW_BOT_NAME
return

:VERIFY_USER_STATUS
setvar $I 1
lowercase $USER_NAME
while ($I <= $CORPYCOUNT)
	cuttext $CORPY[$I] $NAME 1 6
	striptext $NAME " "
	lowercase $NAME
	if ($USER_NAME = $NAME)
		setvar $AUTHORIZATION 1
		return
	end
	add $I 1
end
return

:CHK_LOGIN
if ($LOGGEDIN[$USER_NAME] = 1)
	setvar $LOGGED 1
else
	setvar $LOGGED 0
end
return

:VALIDATION
return

:CALLIN
setvar $NEW_BOT_TEAM_NAME $PARM1
striptext $NEW_BOT_TEAM_NAME "^"
striptext $NEW_BOT_TEAM_NAME " "
lowercase $NEW_BOT_TEAM_NAME
if ($NEW_BOT_TEAM_NAME = "")
	send "'{" $BOT_NAME "} - Invalid team name entered, cannot join that one.*"
	goto :WAIT_FOR_COMMAND
end
setvar $BOT_TEAM_NAME $NEW_BOT_TEAM_NAME
savevar $BOT_TEAM_NAME
send "'{" $BOT_NAME "} - I am now part of team: "&$BOT_TEAM_NAME&"*"
goto :WAIT_FOR_COMMAND

:DOQSETPROTECTIONS
setvar $CANNONTYPE $TYPE
isnumber $TEST $DAMAGE
if ($TEST)
	setvar $CANNONDAMAGE $DAMAGE
else
	send "'{" $BOT_NAME "} - Invalid damage amount entered. *"
	goto :WAIT_FOR_COMMAND
end
return

:QSET

:Q
gosub :CURRENT_PROMPT
setvar $STARTINGLOCATION $CURRENT_PROMPT
setvar $VALIDPROMPTS "Planet Citadel"
gosub :CHECKSTARTINGPROMPT
setvar $TOTALDAMAGE 0
getword $USER_COMMAND_LINE $PARM1 1
getword $USER_COMMAND_LINE $PARM2 2
if (($PARM2 = "a") or ($PARM2 = "s"))
	setvar $TYPE $PARM2
	setvar $DAMAGE $PARM1
elseif (($PARM1 = "a") or ($PARM1 = "s"))
	setvar $TYPE $PARM1
	setvar $DAMAGE $PARM2
else
	setvar $TYPE "s"
	setvar $DAMAGE $PARM1
end
gosub :DOQSETPROTECTIONS
if ($STARTINGLOCATION = "Citadel")
	send "q"
end
gosub :GETPLANETINFO
if ($CITADEL < 3)
	send "'{" $BOT_NAME "} - Planet number " $PLANET " does not have a quasar cannon.*"
	if (($CITADEL > 0) and ($STARTINGLOCATION = "Citadel"))
		send "c "
	end
else
	send "c "
	if ($CANNONTYPE = "s")
		setvar $PERCENTTOSET (((3 * $CANNONDAMAGE) * 100) / $PLANET_FUEL)
		if (((($PLANET_FUEL * $PERCENTTOSET) / 100) / 3) < $CANNONDAMAGE)
			add $PERCENTTOSET 1
		end
		if ($PERCENTTOSET > 100)
			setvar $PERCENTTOSET 100
		end
		add $TOTALDAMAGE ((($PLANET_FUEL * $PERCENTTOSET) / 100) / 3)
		send "l s "&$PERCENTTOSET&"* "
		setvar $DAMAGETYPE "Sector"
	else
		if ($MBBS)
			setvar $PERCENTTOSET ((($CANNONDAMAGE / 2) * 100) / $PLANET_FUEL)
			if (((($PLANET_FUEL * $PERCENTTOSET) / 100) * 2) < $CANNONDAMAGE)
				add $PERCENTTOSET 1
			end
		else
			setvar $PERCENTTOSET (((2 * $CANNONDAMAGE) * 100) / $PLANET_FUEL)
			if (((($PLANET_FUEL * $PERCENTTOSET) / 100) / 2) < $CANNONDAMAGE)
				add $PERCENTTOSET 1
			end
		end
		if ($PERCENTTOSET > 100)
			setvar $PERCENTTOSET 100
		end
		if ($MBBS)
			add $TOTALDAMAGE ((($PLANET_FUEL * $PERCENTTOSET) / 100) * 2)
		else
			add $TOTALDAMAGE ((($PLANET_FUEL * $PERCENTTOSET) / 100) / 2)
		end
		send "l a "&$PERCENTTOSET&"* "
		setvar $DAMAGETYPE "Atmosphere"
	end
	if ($STARTINGLOCATION = "Planet")
		send "q "
	end
	send "'{" $BOT_NAME "} - Quasar Cannon on planet "&$PLANET&" is set to "&$TOTALDAMAGE&". ("&$DAMAGETYPE&")*"
end
goto :WAIT_FOR_COMMAND

:EMX

:RESET
disconnect
goto :WAIT_FOR_COMMAND

:EMQ
send " q q q * p d 0* 0* 0* * *** * c q q q q q z 2 2 c q * z * *** * * "
goto :WAIT_FOR_COMMAND

:LIFT
send "0* 0* 0* q q q q q z a 999* * * * "
waitfor "Command [TL"
send "'I have lifted off from planet " $PLANET "*"
goto :WAIT_FOR_COMMAND

:LOGIN
gosub :KILLTHETRIGGERS
gosub :CURRENT_PROMPT
setvar $STARTINGLOCATION $CURRENT_PROMPT
getwordpos $PARM1 $POS #34
if ($POS > 0)
	gettext " "&$USER_COMMAND_LINE&" " $TRADER_BOT_NAME " "&#34 #34&" "
	if ($TRADER_BOT_NAME <> "")
		lowercase $TRADER_BOT_NAME
		striptext $USER_COMMAND_LINE #34&$TRADER_BOT_NAME&#34
		getword $USER_COMMAND_LINE $LOGIN_BOT_NAME 1
	else
		send "'{" $BOT_NAME "} - Invalid user name, login cannot be completed*"
		goto :WAIT_FOR_COMMAND
	end
else
	if ($PARM1 = 0)
		setvar $VALIDPROMPTS "Citadel Command"
		gosub :CHECKSTARTINGPROMPT
		if ($STARTINGLOCATION = "Command")
			send "t tLogin** q "
		else
			send "x tLogin** q "
		end
		goto :WAIT_FOR_COMMAND
	else
		setvar $TRADER_BOT_NAME $PARM1
		setvar $LOGIN_BOT_NAME $PARM2
	end
end
send "="&$TRADER_BOT_NAME&"*"
settexttrigger PARTIAL :PARTIAL "Do you mean"
settextlinetrigger MATCH :MATCH "Secure comm-link established, Captain."
settextlinetrigger NOMATCH :NOMATCH "Unknown Trader!"
settextlinetrigger YOURSELF :NOMATCH "The crew is disturbed by your incoherent mumbling."
settextlinetrigger NOTONLINE :NOTONLINE "Type M.A.I.L. message"
pause

:NOTONLINE
gosub :KILLTHETRIGGERS
send "* '{"&$BOT_NAME&"} - Syntax Error - Trader doesn't appear to be online!*"
goto :WAIT_FOR_COMMAND

:NOMATCH
gosub :KILLTHETRIGGERS
send "'{"&$BOT_NAME&"} - Syntax Error - Unable to establish communications!*"
goto :WAIT_FOR_COMMAND

:PARTIAL
gosub :KILLTHETRIGGERS
send "y"

:MATCH
gosub :KILLTHETRIGGERS
settextlinetrigger YOURSELF2 :NOMATCH "The crew is disturbed by your incoherent mumbling."
waiton "Type private message [<ENTER>"
send "use "&$LOGIN_BOT_NAME&"**"
settextlinetrigger JOY :JOY "You have 5 seconds to type"
settextlinetrigger JOY2 :JOY "You now have 5 seconds to type"
setdelaytrigger NOJOY :NOJOY 5000
pause

:NOJOY
gosub :KILLTHETRIGGERS
send "'{"&$BOT_NAME&"} - No Response, perhaps Bot is not Loaded.*"
goto :WAIT_FOR_COMMAND

:JOY
gosub :KILLTHETRIGGERS
setvar $_LS_S CURRENTLINE
gettext $_LS_S $_LS_SECURITY_CODE "to type " " on subspace"

:JOY_CONTINUE
if ($_LS_SECURITY_CODE = "")
	send "'{"&$BOT_NAME&"} - Unexpected Response, shutting down login*"
	goto :WAIT_FOR_COMMAND
end
send "'"&$_LS_SECURITY_CODE&"*"
settexttrigger LOGGEDIN :LOGGEDIN "- User Verified -"
setdelaytrigger NOTLOGGEDIN :NOTLOGGEDIN 3000
pause

:NOTLOGGEDIN
gosub :KILLTHETRIGGERS
send "'{"&$BOT_NAME&"} - Security Code Clearance Failed. Make sure comms are on and subspace channels are correct.*"
goto :WAIT_FOR_COMMAND

:LOGGEDIN
gosub :KILLTHETRIGGERS
send "'{"&$BOT_NAME&"} - Security Code Clearance Confirmed!*"
goto :WAIT_FOR_COMMAND
goto :WAIT_FOR_COMMAND

:SLIST
setvar $SCAN_MACRO "x** * "
goto :START_SCAN

:HOLO
setvar $SCAN_MACRO " sh"
goto :START_SCAN

:DSCAN
setvar $SCAN_MACRO " sd"
goto :START_SCAN

:DISP
setvar $SCAN_MACRO "d"
goto :START_SCAN

:START_SCAN
gosub :CURRENT_PROMPT
setarray $SCAN_ARRAY 1000
setvar $STARTINGLOCATION $CURRENT_PROMPT
if (($SCAN_MACRO = "") or ($SCAN_MACRO = 0))
	setvar $SCAN_MACRO " sd* "
end
setvar $VALIDPROMPTS "Citadel Command"
gosub :CHECKSTARTINGPROMPT
if ($STARTINGLOCATION = "Citadel")
	if ($SCAN_MACRO = "d")
		setvar $SCAN_MACRO "s"
	else
		send " q "
		gosub :GETPLANETINFO
		send " q "
	end
end
setvar $IDX 0
settextlinetrigger NOSCANNER_1 :NO_SCANNER_AVAILABLE1 "You don't have a long range scanner."
send $SCAN_MACRO
if ($SCAN_MACRO = "d")
	waiton "<Re-Display>"
elseif ($SCAN_MACRO = "s")
	waiton "<Scan Sector>"
elseif ($SCAN_MACRO = " sd")
	settextlinetrigger NOSCANNER_2 :NO_SCANNER_AVAILABLE2 "Relative Density Scan"
	waiton "Select (H)olo Scan or (D)ensity Scan or (Q)uit? [D] D"
	killtrigger NOSCANNER_1
	killtrigger NOSCANNER_2
elseif ($SCAN_MACRO = "x** * ")
	waiton "Ship  Sect Name                  Fighters Shields Hops Type"
	waiton "--------------------------------------------------------------------------"
else
	waiton "Select (H)olo Scan or (D)ensity Scan or (Q)uit? [D] H"
end
if ($SCAN_MACRO = "s")
	settexttrigger END_OF_LINE2 :END_OF_LINES "Citadel command (?=help)"
	settexttrigger END_OF_LINE3 :END_OF_LINES "Mined Sector: Do you wish to Avoid this sector in the future? (Y/N)"
elseif ($SCAN_MACRO = "x** * ")
	settexttrigger END_OF_LINE4 :END_OF_LINES "<I> Ship details"
	add $IDX 1
	setvar $SCAN_ARRAY[$IDX] "                 --<  Available Ship Scan  >--"
	add $IDX 1
	setvar $SCAN_ARRAY[$IDX] "Ship  Sect Name                  Fighters Shields Hops Type"
	add $IDX 1
	setvar $SCAN_ARRAY[$IDX] "----------------------------------------------------------------------"
else
	settexttrigger END_OF_LINE1 :END_OF_LINES "Command [TL="
end
settextlinetrigger LINE_TRIG :PARSE_SCAN_LINE
pause

:PARSE_SCAN_LINE
setvar $CURRENT_LINE CURRENTLINE
if ($IDX >= 1000)
	goto :END_OF_LINES
end
if (($SCAN_MACRO = "s") or ($SCAN_MACRO = "d"))
	if ($IDX = 0)
		setvar $CURRENT_LINE "-=-=-=-=-=-=-=-=-=-=-=-=-=| Display |=-=-=-=-=-=-=-=-=-=-=-=-=-"
	end
	getwordpos $CURRENT_LINE $POS1 "Citadel treasury contains"
	getwordpos $CURRENT_LINE $POS2 "(?=Help)? :"
	getwordpos $CURRENT_LINE $POS3 "<Re-Display>"
	if (($POS1 < 1) and (($POS2 < 1) and ($POS3 < 1)))
		if (($CURRENT_LINE = "") or ($CURRENT_LINE = 0))
		elseif ($IDX >= 5000)
		else
			add $IDX 1
			replacetext $CURRENT_LINE "Warps to Sector(s) :  " "Warps To: "
			replacetext $CURRENT_LINE "Warps to Sector(s) : " "Warps To: "
			setvar $SCAN_ARRAY[$IDX] $CURRENT_LINE
		end
	end
elseif ($SCAN_MACRO = "x** * ")
	getwordpos $CURRENT_LINE $EM_END "(?=Help)? :"
	if ($EM_END > 0)
		goto :END_OF_LINES
	end
	getwordpos $CURRENT_LINE $EM_END "<I> Ship details"
	if ($EM_END > 0)
		goto :END_OF_LINES
	end
	getlength $CURRENT_LINE $LENGTH
	if ($LENGTH > 70)
		cuttext $CURRENT_LINE $CURRENT_LINE 1 70
	end
	if ($CURRENT_LINE <> "")
		add $IDX 1
		setvar $SCAN_ARRAY[$IDX] $CURRENT_LINE
	end
else
	getwordpos $CURRENT_LINE $EM_END "(?=Help)? :"
	if ($EM_END > 0)
		goto :END_OF_LINES
	end
	getwordpos $CURRENT_LINE $POS "One turn deducted,"
	if ($POS > 0)
		setvar $CURRENT_LINE "-=-=-=-=-=-=-=-=-=-=-=-=-| Holo Scan |-=-=-=-=-=-=-=-=-=-=-=-=-"
	end
	getwordpos $CURRENT_LINE $POS "Relative Density Scan"
	if ($POS > 0)
		setvar $CURRENT_LINE "-=-=-=-=-=-=-=-=-=-| Relative Density Scan |-=-=-=-=-=-=-=-=-=-"
	end
	if (($CURRENT_LINE = "") or ($CURRENT_LINE = 0))
		goto :BOGUS
	end
	getwordpos $CURRENT_LINE $POS "Sector  :"
	if ($POS > 0)
		add $IDX 1
		setvar $SCAN_ARRAY[$IDX] "    "
	end
	getwordpos $CURRENT_LINE $POS1 "-------"
	getwordpos $CURRENT_LINE $POS2 "Long Range Scan"
	getwordpos $CURRENT_LINE $POS3 "Select (H)olo Scan or (D)ensity Scan or (Q)uit?"
	getwordpos $CURRENT_LINE $POS4 "<Mine Control>"
	getwordpos $CURRENT_LINE $POS5 "(?=Help)? :"
	if (($POS1 < 1) and (($POS2 < 1) and (($POS3 < 1) and (($POS4 < 1) and ($POS5 < 1)))))
		replacetext $CURRENT_LINE "Warps to Sector(s) :  " "Warps To: "
		replacetext $CURRENT_LINE "Warps to Sector(s) : " "Warps To: "
		replacetext $CURRENT_LINE " ==>    " " => "
		replacetext $CURRENT_LINE "  Warps : " "  Warps: "
		replacetext $CURRENT_LINE "   NavHaz :   " " Haz: "
		replacetext $CURRENT_LINE "  Anom : " " Anom: "
		add $IDX 1
		setvar $SCAN_ARRAY[$IDX] $CURRENT_LINE
	end

	:BOGUS
end
settextlinetrigger LINE_TRIG :PARSE_SCAN_LINE
pause

:END_OF_LINES
gosub :KILLTHETRIGGERS
if ($STARTINGLOCATION = "Citadel")
	if (($SCAN_MACRO = "d") or ($SCAN_MACRO = "s"))
		send "* "
	else
		send " l "&$PLANET&"* c s* "
	end
end
gosub :SPITITOUT
goto :WAIT_FOR_COMMAND

:NO_SCANNER_AVAILABLE1
send "'{" $BOT_NAME "} - No scanner available.** "
goto :WAIT_FOR_COMMAND

:NO_SCANNER_AVAILABLE2
setvar $CURRENT_LINE "-=-=-=-=-=-=-=-=-=-| Relative Density Scan |-=-=-=-=-=-=-=-=-=-"
add $IDX 1
setvar $SCAN_ARRAY[$IDX] $CURRENT_LINE
settextlinetrigger LINE_TRIG :PARSE_SCAN_LINE
pause

:HANDLE_MINES
send "*"
goto :END_OF_LINES

:PSCAN
setarray $SCAN_ARRAY 30
gosub :QUIKSTATS
setvar $LOCATION $CURRENT_PROMPT
setvar $PLANET 0
isnumber $TEST $PARM1
if (($LOCATION = "Citadel") or ($LOCATION = "Planet"))
	if ($LOCATION = "Citadel")
		send "Q  "
	end
	if (($PARM1 <> 0) and ($TEST = TRUE))
		gosub :GETPLANETINFO
		send "  Q  "
		setvar $LANDON $PARM1
		gosub :DO_PSCAN
		setvar $LANDON $PLANET
		gosub :LAND_ONPLANET
	else
		waitfor "Planet command"
		gosub :START_PSCAN
	end
	if ($LOCATION = "Citadel")
		send " C  "
	end
elseif ($LOCATION = "Command")
	if (($PARM1 = 0) or ($TEST = FALSE))
		send "'{" $BOT_NAME "} PScan - If Starting From Sector Please Specify Planet Number.*"
		goto :WAIT_FOR_COMMAND
	end
	setvar $LANDON $PARM1
	gosub :DO_PSCAN
else
	send "'{"&$BOT_NAME&"} PScan - Please Start from Command, Citadel, or Planet Prompt*"
end
if ($GOTSCAN)
	gosub :SPITITOUT
end
goto :WAIT_FOR_COMMAND

:START_PSCAN
setvar $IDX 0
send "D"

:CONTINUEPSCAN
settexttrigger DONE :PSCAN_DONE "Planet command"
settextlinetrigger LINE_TRIG :PARSE_PSCAN_LINE
pause

:PARSE_PSCAN_LINE
killtrigger LINE_TRIG
setvar $S CURRENTLINE
if (($S = "") or ($S = 0))
	setvar $S "          "
end
getwordpos $S $POS "Fuel Ore"
gosub :DOPSCANTEXT
getwordpos $S $POS "Organics"
gosub :DOPSCANTEXT
getwordpos $S $POS "Equipment"
gosub :DOPSCANTEXT
getwordpos $S $POS "Fighters "
gosub :DOPSCANTEXT
replacetext $S "  Item    Colonists  Colonists    Daily     Planet      Ship      Planet" "Item  Colonists Colonists    Daily     Planet    Planet"
replacetext $S "           (1000s)   2 Build 1   Product    Amount     Amount     Maximum" "       (1000s)  2 Build 1   Product    Amount    Maximum"
replacetext $S " -------  ---------  ---------  ---------  ---------  ---------  ---------" "---  ---------  ---------  ---------  ---------  ---------"
replacetext $S "Fuel Ore" "Ore"
replacetext $S "Organics" "Org"
replacetext $S "Equipment" "Equ "
replacetext $S "Fighters " "Figs"
replacetext $S "Military reaction" "Mil-React"
add $IDX 1
setvar $SCAN_ARRAY[$IDX] $S
settextlinetrigger LINE_TRIG :PARSE_PSCAN_LINE
pause

:PSCAN_DONE
gosub :KILLTHETRIGGERS
setvar $GOTSCAN TRUE
return

:DOPSCANTEXT
if ($POS <> 0)
	cuttext $S $S_TEMP1 1 53
	cuttext $S $S_TEMP2 65 75
	setvar $S $S_TEMP1&$S_TEMP2
end
return

:DO_PSCAN
gosub :LAND_ONPLANET
if ($LANDED)
	gosub :START_PSCAN
	setvar $GOTSCAN TRUE
else
	send " Q  Q  Q  Z  N  *  L Z"&#8&$PLANET&"*  *  J  C  *  "
	send "'{" $BOT_NAME "} PScan - Problem landing on Planet #"&$PARM1&".*"
	setvar $GOTSCAN FALSE
end
send " Q  Q  Q  Z  N  *  "
return

:SPITITOUT
setvar $I 1
getwordpos $USER_COMMAND_LINE $POS "fed"
if ($POS > 0)
	send "`*"
else
	send "'*"
end
if ($POS > 0)
	settextlinetrigger COMM :CONTINUECOMMPSCAN "Federation comm-link established, Captain."
else
	settextlinetrigger COMM :CONTINUECOMMPSCAN "Comm-link open on sub-space band"
end
pause

:CONTINUECOMMPSCAN
while ($I <= $IDX)
	if ($SCAN_ARRAY[$I] <> 0)
		send $SCAN_ARRAY[$I]&"*"
	end
	add $I 1
end
send "*  "
if ($POS > 0)
	settextlinetrigger COMM3 :CONTINUECOMMPSCAN2 "Federation comm-link terminated."
else
	settextlinetrigger COMM3 :CONTINUECOMMPSCAN2 "Sub-space comm-link terminated"
end
pause

:CONTINUECOMMPSCAN2
return

:LAND_ONPLANET
setvar $LANDED FALSE
send "L"&$LANDON&"*Z  N  Z  N  *  "
settextlinetrigger NOPLANET1 :NOPLANET "There isn't a planet in this sector."
settextlinetrigger NOPLANET2 :NOPLANET "That planet is not in this sector."
settextlinetrigger NOTLANDED :NOTLANDED "since it couldn't possibly stand"
settextlinetrigger LANDED :LANDED "Planet #"
pause

:NOPLANET
gosub :KILLTHETRIGGERS
send "'{"&$BOT_NAME&"} - Planet #"&$LANDON&", not in Sector!*"
return

:NOTLANDED
gosub :KILLTHETRIGGERS
send "'{"&$BOT_NAME&"} - This ship cannot land!*"
return

:LANDED
gosub :KILLTHETRIGGERS
setvar $LANDED TRUE
waitfor "<Destroy Planet>"
waitfor "Planet command"
return

:TOPOFF
gosub :KILLTHETRIGGERS
gosub :CURRENT_PROMPT
setvar $VALIDPROMPTS "Citadel Command"
gosub :CHECKSTARTINGPROMPT
if ($STARTINGLOCATION = "Citadel")
	send " q "
	gosub :GETPLANETINFO
	send " q "
end
if (($PARM1 <> "o") and (($PARM1 <> "t") and ($PARM1 <> "d")))
	setvar $TYPE "d"
	isnumber $TEST CURRENTSECTOR
	if ($TEST = TRUE)
		if ((CURRENTSECTOR > 0) and (CURRENTSECTOR <= SECTORS))
			setvar $TYPE SECTOR.FIGS.TYPE[CURRENTSECTOR]
			if ($TYPE = "Offensive")
				setvar $TYPE "o"
			elseif ($TYPE = "Defensive")
				setvar $TYPE "d"
			elseif ($TYPE = "Toll")
				setvar $TYPE "t"
			else
				setvar $TYPE "d"
			end
		end
	end
	setvar $PARM1 $TYPE
end
setvar $TO_DROP $PARM1
gosub :DO_TOPOFF
if ($STARTINGLOCATION = "Citadel")
	gosub :LANDINGSUB
end
send "'{" $BOT_NAME "} - TopOff complete Left "&$FTRS_TO_LEAVE " fighters.*"
goto :WAIT_FOR_COMMAND

:DO_TOPOFF

:DO_TOPOFF_AGAIN
gosub :KILLTHETRIGGERS
send " F"
waiton "Your ship can support up to"
getword CURRENTLINE $FTRS_TO_LEAVE 10
striptext $FTRS_TO_LEAVE ","
striptext $FTRS_TO_LEAVE " "
if ($FTRS_TO_LEAVE < 1)
	setvar $FTRS_TO_LEAVE 1
end
send " "&$FTRS_TO_LEAVE&" * C "&$TO_DROP
settextlinetrigger TOPOFF_SUCCESS :TOPOFF_SUCCESS "Done. You have "
settextlinetrigger TOPOFF_FAILURE1 :DO_TOPOFF_AGAIN "You don't have that many fighters available."
settextlinetrigger TOPOFF_FAILURE2 :DO_TOPOFF_AGAIN "Too many fighters in your fleet!  You are limited to"
pause

:TOPOFF_SUCCESS
return

:CLEARBUSTS
delete $BUST_FILE
setvar $I 1
while ($I <= SECTORS)
	setsectorparameter $I "BUSTED" FALSE
	add $I 1
end
setvar $MESSAGE "Bust file for this bot has been cleared.*"
gosub :SWITCHBOARD
goto :WAIT_FOR_COMMAND

:MEGA
setvar $ISMEGA TRUE

:ROB
gosub :QUIKSTATS
setvar $VALIDPROMPTS "Citadel Command"
gosub :CHECKSTARTINGPROMPT
cuttext $ALIGNMENT $NEG_CK 1 1
striptext $ALIGNMENT "-"
if (($ALIGNMENT < 100) and ($NEG_CK = "-")) or ($NEG_CK <> "-")
	send "'{" $BOT_NAME "} - Need -100 Alignment Minimum*"
	goto :PORTRM_DONE
end
if ($STARTINGLOCATION = "Citadel")
	send "q"
	gosub :GETPLANETINFO
	send "q"
end
setvar $SECOND_MEGA 0
setvar $LEFTOVER_CASH 0
setvar $MEGA_MIN 2970000
setvar $MEGA_MAX 5760000
send "p r * r"
settextlinetrigger FAKE :PORT_FAKE "Busted!"
settextlinetrigger MEGA :PORT_OK "port has in excess of"
pause

:PORT_FAKE
gosub :KILLTHETRIGGERS
if ($STARTINGLOCATION = "Citadel")
	gosub :LANDINGSUB
end
send "'{" $BOT_NAME "} - Fake Busted*"
goto :PORTRM_DONE

:PORT_OK
gosub :KILLTHETRIGGERS
setvar $ROB ($ROB_FACTOR * $EXPERIENCE)
getword CURRENTLINE $PORT_CASH 11
striptext $PORT_CASH ","
if ($PORT_CASH < $MEGA_MIN)
	if ($ISMEGA)
		setvar $PORT_CASH (($PORT_CASH * 10) / 9)
		setvar $MEGA_SHORT (3300000 - $PORT_CASH)
		send "0* "
		if ($STARTINGLOCATION = "Citadel")
			gosub :LANDINGSUB
		end
		send "'{" $BOT_NAME "} - Port is short " $MEGA_SHORT " credits*"
		goto :PORTRM_DONE
	else
		goto :DO_ROB
	end
elseif (($MBBS = TRUE) and ($ISMEGA = FALSE))
	send "'{" $BOT_NAME "} - " $PORT_CASH " credits on port.  Port is ready for Mega Rob**"
	if ($STARTINGLOCATION = "Citadel")
		gosub :LANDINGSUB
	end
	goto :PORTRM_DONE
else
	if ($ISMEGA)
		setvar $ACTUAL_CASH $PORT_CASH
		multiply $ACTUAL_CASH 10
		divide $ACTUAL_CASH 9
		setvar $MEGA_CASH $ACTUAL_CASH
		if ($ACTUAL_CASH >= 3300000)

		:MEGA_LOOP
			if ($MEGA_CASH > 6400000)
				subtract $MEGA_CASH 3300000
				add $LEFTOVER_CASH 3300000
				setvar $SECOND_MEGA 1
				goto :MEGA_LOOP
			end
			if ($SECOND_MEGA = 0)
				send $ACTUAL_CASH "*"
			elseif ($SECOND_MEGA = 1)
				send $MEGA_CASH "*"
				setvar $ACTUAL_CASH $MEGA_CASH
			end
		end
		settextlinetrigger MEGA_SUC :PORT_SUC "Success!"
		settextlinetrigger MEGA_BUST :PORT_BUST "Busted!"
		pause
	else
		goto :DO_ROB
	end
end

:PORT_BUST
gosub :KILLTHETRIGGERS
if ($STARTINGLOCATION = "Citadel")
	gosub :LANDINGSUB
end
send "'{" $BOT_NAME "} - Busted*"
goto :PORTRM_DONE

:PORT_SUC
gosub :KILLTHETRIGGERS
if ($STARTINGLOCATION = "Citadel")
	gosub :LANDINGSUB
	send "tt" $ACTUAL_CASH "*"
end
send "'{" $BOT_NAME "} - Success! - " $ACTUAL_CASH " credits robbed*"
if ($SECOND_MEGA = TRUE)
	send "'{" $BOT_NAME "} - There are " $LEFTOVER_CASH " credits left for a second mega*"
end

:PORTRM_DONE
setvar $ISMEGA FALSE
goto :WAIT_FOR_COMMAND

:DO_ROB
setvar $PORT_CASH (($PORT_CASH * 10) / 9)
if ($PORT_CASH < $ROB)
	setvar $ROB $PORT_CASH
end
send $ROB "*"
setvar $ACTUAL_CASH $ROB
settextlinetrigger PORT_EMPTY :PORT_SUC "Maybe some other day, eh?"
settextlinetrigger MEGA_SUC :PORT_SUC "Success!"
settextlinetrigger PORT_BUST :PORT_BUST "Busted!"
pause

:PING
setvar $PINGS 0
loadvar $TOTAL_PINGS
if ($TOTAL_PINGS = 0)
	setvar $TOTAL_PINGS 4
	savevar $TOTAL_PINGS
end
setvar $TOTAL_PING_TIMES 0
setvar $PING_DELAY 500
send "'*{" $BOT_NAME "} - *"
waiton "Type sub-space message"

:SEND_PINGS
setdelaytrigger PING_DELAY :PING_DELAY $PING_DELAY
pause

:PING_DELAY
add $PINGS 1
if ($PINGS <= $TOTAL_PINGS)
	gettime $START_TIME "ss zzz"
	send "ping "
	waiton "S: ping"
	gettime $STOP_TIME "ss zzz"
	getword $START_TIME $START_SEC 1
	getword $START_TIME $START_MS 2
	getword $STOP_TIME $STOP_SEC 1
	getword $STOP_TIME $STOP_MS 2
	if ($STOP_SEC < $START_SEC)
		add $STOP_SEC 60
	end
	setvar $SEC_DIFF $STOP_SEC
	subtract $SEC_DIFF $START_SEC
	multiply $SEC_DIFF 1000
	add $STOP_MS $SEC_DIFF
	setvar $PING $STOP_MS
	subtract $PING $START_MS
	add $TOTAL_PING_TIMES $PING
	send $PING&"*"
	goto :SEND_PINGS
end
send "*"
waiton "Sub-space comm-link terminated"
divide $TOTAL_PING_TIMES $TOTAL_PINGS
send "'{" $BOT_NAME "} - avg ping - "&$TOTAL_PING_TIMES&"*"
goto :WAIT_FOR_COMMAND

:X

:XPORT
gosub :KILLTHETRIGGERS
gosub :QUIKSTATS
if (($UNLIMITEDGAME = 0) and ($TURNS < 1))
	send "'{" $BOT_NAME "} - Don't have any turns left!"
	goto :WAIT_FOR_COMMAND
end
setvar $STARTINGLOCATION $CURRENT_PROMPT
setvar $VALIDPROMPTS "Citadel Command Planet"
gosub :CHECKSTARTINGPROMPT
isnumber $RESULT $PARM1
isnumber $SAFESHIP_RESULT $SAFE_SHIP
if ($RESULT < 1)
	send "'{" $BOT_NAME "} - xport [ship number] [password]*"
	goto :WAIT_FOR_COMMAND
end
if (($PARM1 < 1) and ($SAFESHIP_RESULT >= 1))
	if ($SAFE_SHIP > 0)
		setvar $PARM1 $SAFE_SHIP
	else
		send "'{" $BOT_NAME "} - Safeship parameter not defined correctly.*"
		goto :WAIT_FOR_COMMAND
	end
end
if ($STARTINGLOCATION = "Citadel")
	if ($PLANET = 0)
		send " q "
		gosub :GETPLANETINFO
		send " q "
	else
		send "qq   "
	end
elseif ($STARTINGLOCATION = "Planet")
	if ($PLANET = 0)
		gosub :GETPLANETINFO
	end
	send " q "
else
	setvar $PLANET 0
end
settextlinetrigger BAD_SHIP_TRIG :SHIP_NOT_AVAILABLE "That is not an available ship."
settextlinetrigger BAD_RANGE_TRG :OUT_OF_RANGE "only has a transport range of"
settextlinetrigger CANNOT_XPORT :CANNOT_XPORT "Access denied!"
settexttrigger XPORT_PASSW :XPORT_PASSWORD "Enter the password for"
settextlinetrigger XPORT_GOOD :XPORT_GOOD "Security code accepted, engaging transporter control."
if ($PARM2 = 0)
	send "x   "&$PARM1&"*    "
else
	send "x  "&$PARM1&"*"
end
pause

:SHIP_NOT_AVAILABLE
setvar $MESSAGE "That ship is not available.*"
goto :OUT_OF_XPORT

:OUT_OF_RANGE
setvar $MESSAGE "That ship is out of range.*"
goto :OUT_OF_XPORT

:XPORT_GOOD
setvar $MESSAGE "Xport complete.*"
if ($COMMAND = "x")
	setvar $SAFE_SHIP $SHIP_NUMBER
	echo "*" ANSI_14 "[" ANSI_15 "Safe ship auto-set to last ship: " $SHIP_NUMBER ANSI_14 "]*" ANSI_7
end
goto :OUT_OF_XPORT

:XPASS_BAD
setvar $MESSAGE "Incorrect ship password!*"
waitfor "Choose which ship to beam to"
goto :OUT_OF_XPORT

:CANNOT_XPORT
setvar $MESSAGE "Cannot xport to that ship!*"
goto :OUT_OF_XPORT

:XPORT_PASSWORD
gosub :KILLTHETRIGGERS
settextlinetrigger XPORT_OK :XPORT_GOOD "Security code accepted, engaging transporter control."
settextlinetrigger XPASS_BAD :XPASS_BAD "SECURITY BREACH! Invalid Password, unable to link transporters."
send $PARM2&"*   "
pause

:OUT_OF_XPORT
gosub :KILLTHETRIGGERS
send "    *    "
if ((($STARTINGLOCATION = "Citadel") or ($STARTINGLOCATION = "Planet")) and ($PLANET <> 0))
	gosub :LANDINGSUB
end
echo "**"
gosub :SWITCHBOARD
goto :WAIT_FOR_COMMAND

:MAXPORT

:MAX
gosub :KILLTHETRIGGERS
gosub :QUIKSTATS
setvar $STARTINGLOCATION $CURRENT_PROMPT
setvar $VALIDPROMPTS "Citadel Command Planet"
gosub :CHECKSTARTINGPROMPT
if (($PARM1 <> "f") and (($PARM1 <> "o") and ($PARM1 <> "e")))
	send "'{" $BOT_NAME "} - maxport [f/o/e] noexp*"
	goto :WAIT_FOR_COMMAND
end
setvar $TOTAL_CREDS_NEEDED 0
if (($STARTINGLOCATION = "Planet") or ($STARTINGLOCATION = "Citadel"))
	if ($STARTINGLOCATION = "Citadel")
		send "q"
	end
	gosub :GETPLANETINFO
	if ($CITADEL > 0)
		send "cs* cr*q"
		waiton "<Enter Citadel>"
		waiton "Fuel Ore"
		getword CURRENTLINE $PORTFUEL 4
		getword CURRENTLINE $PORTFUELPERCENT 5
		striptext $PORTFUELPERCENT "%"
		waiton "Organics"
		getword CURRENTLINE $PORTORG 3
		getword CURRENTLINE $PORTORGPERCENT 4
		striptext $PORTORGPERCENT "%"
		waiton "Equipment"
		getword CURRENTLINE $PORTEQUIP 3
		getword CURRENTLINE $PORTEQUIPPERCENT 4
		striptext $PORTEQUIPPERCENT "%"
		if ($PORTEQUIPPERCENT <= 0)
			setvar $PORTEQUIPPERCENT 1
		end
		if ($PORTORGPERCENT <= 0)
			setvar $PORTORGPERCENT 1
		end
		if ($PORTFUELPERCENT <= 0)
			setvar $PORTFUELPERCENT 1
		end
		setvar $TOTALFUELUPGRADENEEDED ((($PORT_MAX - (($PORTFUEL * 100) / $PORTFUELPERCENT)) / 10) + 1)
		setvar $TOTALORGUPGRADENEEDED ((($PORT_MAX - (($PORTORG * 100) / $PORTORGPERCENT)) / 10) + 1)
		setvar $TOTALEQUIPUPGRADENEEDED ((($PORT_MAX - (($PORTEQUIP * 100) / $PORTEQUIPPERCENT)) / 10) + 1)
		if ($PARM1 = "f")
			setvar $TOTAL_CREDS_NEEDED (300 * $TOTALFUELUPGRADENEEDED)
		elseif ($PARM1 = "o")
			setvar $TOTAL_CREDS_NEEDED (500 * $TOTALORGUPGRADENEEDED)
		else
			setvar $TOTAL_CREDS_NEEDED (1000 * $TOTALEQUIPUPGRADENEEDED)
		end
		if ($TOTAL_CREDS_NEEDED > $CREDITS)
			setvar $CASHONHAND $CITADEL_CREDITS
			add $CASHONHAND $CREDITS
			if ($CASHONHAND > $TOTAL_CREDS_NEEDED)
				if ($STARTINGLOCATION = "Planet")
					send "C"
				end
				send "T T "&$CREDITS&"* "
				send "T F "&$TOTAL_CREDS_NEEDED&"* "
				setvar $CREDITS $TOTAL_CREDS_NEEDED
				send "'{" $BOT_NAME "} - Withdrew funds from the Treasury to complete the port max*"
			end
		end
		send "q q"
	else
		send "q"
	end
end
if ($PARM2 = "noexp")
	setvar $NO_EXP TRUE
else
	setvar $NO_EXP FALSE
end
if ($PARM1 = "f")
	setvar $PRODUCT 1
	setvar $NOEXPAMOUNT 9
end
if ($PARM1 = "o")
	setvar $PRODUCT 2
	setvar $NOEXPAMOUNT 4
end
if ($PARM1 = "e")
	setvar $PRODUCT 3
	setvar $NOEXPAMOUNT 3
end
gosub :DOMAXPORT
if (($STARTINGLOCATION = "Citadel") or ($STARTINGLOCATION = "Planet"))
	gosub :LANDINGSUB
end
send "'{" $BOT_NAME "} - Port upgrade complete.*"
goto :WAIT_FOR_COMMAND

:DOMAXPORT
send "o " $PRODUCT "0* "
settextlinetrigger NOREALPORTHERE :DONEMAXPORT "Do you want to initiate construction on this port?"
waiton ", 0 to quit)"
gosub :KILLTHETRIGGERS
getword CURRENTLINE $UPGRADEAMOUNT 9
striptext $UPGRADEAMOUNT "("
send "o "
if ($NO_EXP)
	while ($UPGRADEAMOUNT > 0)
		if ($UPGRADEAMOUNT > 3)
			send $PRODUCT " " $NOEXPAMOUNT "* "
			subtract $UPGRADEAMOUNT $NOEXPAMOUNT
		else
			send $PRODUCT " " $UPGRADEAMOUNT "* "
			subtract $UPGRADEAMOUNT $UPGRADEAMOUNT
		end
	end
	send "* * "
else
	send $PRODUCT " " $UPGRADEAMOUNT "* * "
end
send "CR*Q"
waiton "<Computer deactivated>"

:DONEMAXPORT
gosub :KILLTHETRIGGERS
return

:PGRID
gosub :QUIKSTATS
setvar $STARTINGLOCATION $CURRENT_PROMPT
setvar $STARTINGPGRIDSECTOR $CURRENT_SECTOR
setvar $STARTINGSHIP $SHIP_NUMBER
setvar $VALIDPROMPTS "Citadel Command"
gosub :CHECKSTARTINGPROMPT
if ($STARTINGLOCATION = "Citadel")
	setvar $INCITADEL "Q Q "
else
	setvar $INCITADEL ""
end
getwordpos " "&$USER_COMMAND_LINE&" " $POS "scan"
if ($POS > 0)
	setvar $DODENSITYSCAN TRUE
else
	setvar $DODENSITYSCAN FALSE
end
getwordpos " "&$USER_COMMAND_LINE&" " $POS " x:"
if ($POS > 0)
	gettext $USER_COMMAND_LINE $XPORTSHIP "x:" " "
	isnumber $TEST $XPORTSHIP
	if ($TEST)
		setvar $XPORTING TRUE
	else
		send "'{" $BOT_NAME "} - Invalid xport ship entered*"
		goto :WAIT_FOR_COMMAND
	end
else
	setvar $XPORTING FALSE
end
getwordpos " "&$USER_COMMAND_LINE&" " $POS " r "
if ($POS > 0)
	setvar $RETREATING TRUE
else
	setvar $RETREATING FALSE
end
setvar $PGRIDSECTOR $PARM1
isnumber $TEST $PGRIDSECTOR
if ($TEST = 0)
	send "'{" $BOT_NAME "} - Invalid PGRID number.*"
	goto :WAIT_FOR_COMMAND
end
isnumber $TEST $PARM2
if ($TEST = 0)
	setvar $WAVECOUNT 1
else
	if ($PARM2 > 0)
		setvar $WAVECOUNT $PARM2
	else
		setvar $WAVECOUNT 1
	end
end
if ($PGRIDSECTOR = 0)
	send "'{" $BOT_NAME "} - Invalid PGRID number.*"
	goto :WAIT_FOR_COMMAND
end
if ($PGRIDSECTOR < 11)
	send "'{" $BOT_NAME "} - Cannot PGRID into FedSpace!*"
	goto :WAIT_FOR_COMMAND
elseif ($PGRIDSECTOR = $STARDOCK)
	send "'{" $BOT_NAME "} - Cannot PGRID into STARDOCK!*"
	goto :WAIT_FOR_COMMAND
end
if ($STARTINGLOCATION = "Citadel")
	send "q"
	gosub :GETPLANETINFO
	send "c "
end
if ($SHIP_MAX_ATTACK <= 0)
	gosub :GETSHIPSTATS
end
setvar $I 1
setvar $ISFOUND FALSE
while (SECTOR.WARPS[$CURRENT_SECTOR][$I] > 0)
	if (SECTOR.WARPS[$CURRENT_SECTOR][$I] = $PGRIDSECTOR)
		setvar $ISFOUND TRUE
	end
	add $I 1
end
if ($ISFOUND = FALSE)
	send "'{" $BOT_NAME "} - Cannot PGRID.  Sector "&$PGRIDSECTOR&" not Adjacent, aborting..*"
	goto :WAIT_FOR_COMMAND
end
send "'{" $BOT_NAME "} - Planet gridding into sector "&$PGRIDSECTOR&"* c v* y* "&$PGRIDSECTOR&"* q "
setvar $MAC "     * "
if ($WAVECOUNT <= 0)
	setvar $WAVECOUNT 1
end
if ($FIGHTERS < $SHIP_MAX_ATTACK)
	setvar $MAC $MAC&"a z "&($FIGHTERS - 1)&9999&"* * "
else
	setvar $I 1
	while (($I <= $WAVECOUNT) and ($FIGHTERS >= $SHIP_MAX_ATTACK))
		setvar $MAC $MAC&"a z "&($SHIP_MAX_ATTACK - 1)&9999&"* * "
		add $I 1
		subtract $FIGHTERS ($SHIP_MAX_ATTACK - 1)
	end
end
setvar $MAC $MAC&"j r * f  z  1  * z  c  d  * "
setvar $PREVIOUSPLANETSINSECTOR SECTOR.PLANETCOUNT[$CURRENT_SECTOR]
if ($DODENSITYSCAN = TRUE)
	send "s* "
end
if (($SCAN_TYPE <> "None") and ($DODENSITYSCAN = TRUE))

:DENSITY_SCANNING
	setvar $TEMPDENSITY SECTOR.DENSITY[$PGRIDSECTOR]
	setvar $PGRIDDENSITY "-99"
	send "q q sdz* l " $PLANET "* c  "
	waiton "Relative Density Scan"
	settextlinetrigger DENSCHECK :GETDENSITYPGRID " "&$PGRIDSECTOR&"  ==>"
	settextlinetrigger DENSCHECK2 :GETDENSITYPGRID2 " "&$PGRIDSECTOR&") ==>"
	settextlinetrigger DENSCHECK3 :GETDENSITYPGRID "("&$PGRIDSECTOR&") ==>"
	settextlinetrigger DENSCHECKDONE :DONEDENSITYCHECK "<Enter Citadel>"
	pause

	:GETDENSITYPGRID
	killtrigger DENSCHECK
	killtrigger DENSCHECK3
	killtrigger DENSCHECK2
	getword CURRENTLINE $PGRIDDENSITY 4
	striptext $PGRIDDENSITY ","
	pause

	:GETDENSITYPGRID2
	killtrigger DENSCHECK
	killtrigger DENSCHECK3
	killtrigger DENSCHECK2
	getword CURRENTLINE $PGRIDDENSITY 5
	striptext $PGRIDDENSITY ","
	pause

	:DONEDENSITYCHECK
	gosub :BIGDELAY_KILLTHETRIGGERS
	if ($TEMPDENSITY <> "-1")
		if ($PGRIDDENSITY = "-99")
			setvar $MESSAGE "Last Density Scan was not correctly grabbed, cannot safely continue.*"
			gosub :SWITCHBOARD
			goto :WAIT_FOR_COMMAND
		elseif ($PGRIDDENSITY > $TEMPDENSITY)
			setvar $MESSAGE "Density increased since last scan in sector "&$PGRIDSECTOR&". ("&$PGRIDDENSITY&")*"
			gosub :SWITCHBOARD
			goto :WAIT_FOR_COMMAND
		end
	else
		setvar $MESSAGE "You must density scan sector "&$PGRIDSECTOR&" at least once before pgridding.*"
		gosub :SWITCHBOARD
		goto :WAIT_FOR_COMMAND
	end
end
setvar $NEWPLANETSINSECTOR SECTOR.PLANETCOUNT[$CURRENT_SECTOR]
if (($PREVIOUSPLANETSINSECTOR < $NEWPLANETSINSECTOR) and ($NEWPLANETSINSECTOR > 1))
	setvar $MESSAGE "Planet number increased since last scan in this sector. Try again to override.*"
	gosub :SWITCHBOARD
	goto :WAIT_FOR_COMMAND
end
if ($RETREATING)
	send $INCITADEL&"m "&$PGRIDSECTOR&$MAC&"< n n n * "
	if ($PLANET > 0)
		send "l j"&#8&$PLANET&"*  *  "
	end
	gosub :QUIKSTATS
	if ($CURRENT_SECTOR <> $STARTINGPGRIDSECTOR)
		send "'"&$PGRIDSECTOR&"=saveme* "
		gosub :EMERGENCYLANDING
		send "'{" $BOT_NAME "} - Unsuccessful retreat from sector "&$PGRIDSECTOR&". Attempted saveme call.*"
	else
		if ($CURRENT_PROMPT = "Planet")
			send "m * * * c p "&$PGRIDSECTOR&"* y s* "
		end
		gosub :QUIKSTATS
		if ($CURRENT_SECTOR = $PGRIDSECTOR)
			send "'{" $BOT_NAME "} - Successfully P-gridded into sector "&$PGRIDSECTOR&"*"
			setvar $TARGET $PGRIDSECTOR
			gosub :ADDFIGTODATA
		else
			send "'{" $BOT_NAME "} - No fighter deployed in sector "&$PGRIDSECTOR&"*"
		end
	end
else
	setvar $PGRIDSTRING "'"&$PGRIDSECTOR&"=saveme* "&$INCITADEL&"m "&$PGRIDSECTOR&$MAC
	if ($XPORTING)
		setvar $PGRIDSTRING $PGRIDSTRING&"x   "&$XPORTSHIP&"* * "
	end
	send $PGRIDSTRING
	if ($XPORTING)
		gosub :QUIKSTATS
		if ($SHIP_NUMBER = $STARTINGSHIP)
			gosub :EMERGENCYLANDING
			send "'{" $BOT_NAME "} - Unsuccessful xport out of sector "&$PGRIDSECTOR&". Ship too far away or I was photoned.*"
		else
			if ($CURRENT_SECTOR = $STARTINGPGRIDSECTOR)
				gosub :EMERGENCYLANDING
				send "'{" $BOT_NAME "} - Unsuccessful pgrid unless xport ship was in starting sector. Currently in xport ship.*"
			else
				getrnd $XDELAY 500 2000
				setdelaytrigger WAITPGRIDXPORT :GOPGRIDXPORT $XDELAY
				pause

				:GOPGRIDXPORT
				send "x   "&$STARTINGSHIP&"* * l j"&#8&$PLANET&"*  *  "
				gosub :QUIKSTATS
				if ($CURRENT_PROMPT = "Planet")
					send "m * * * c s* "
				end
				if ($SHIP_NUMBER <> $STARTINGSHIP)
					send "'{" $BOT_NAME "} - Gridding ship not available for re-export.  Bot is in safe ship.*"
				else
					send "'{" $BOT_NAME "} - Successfully P-gridded w/xport into sector "&$PGRIDSECTOR&"*"
				end
			end
		end
	else
		gosub :EMERGENCYLANDING
		gosub :QUIKSTATS
		if ($CURRENT_SECTOR <> $PGRIDSECTOR)
			send "'{" $BOT_NAME "} - Unsuccessful P-grid into sector "&$PGRIDSECTOR&". Someone make sure bot is picked up.*"
		else
			send "'{" $BOT_NAME "} - Successfully P-gridded into sector "&$PGRIDSECTOR&"*"
			setvar $TARGET $PGRIDSECTOR
			gosub :ADDFIGTODATA
		end
	end
end
goto :WAIT_FOR_COMMAND

:EMERGENCYLANDING
setvar $I 0
while ($I < 30)
	add $I 1
	send "l j"&#8&$PLANET&"*  *  "
end
gosub :CURRENT_PROMPT
if ($CURRENT_PROMPT = "Planet")
	send "m * * * c s* "
end
return

:HTORP
gosub :KILLTHETRIGGERS
gosub :QUIKSTATS
if ($SCAN_TYPE <> "Holo")
	send "'{" $BOT_NAME "} - You can not run htorp without a holographic scanner.*"
	goto :WAIT_FOR_COMMAND
end
setvar $STARTINGLOCATION $CURRENT_PROMPT
if ($STARTINGLOCATION = "Command")
elseif ($STARTINGLOCATION = "Citadel")
	send "q "
	gosub :GETPLANETINFO
else
	echo "*Wrong prompt for htorp.*"
	goto :WAIT_FOR_COMMAND
end
if ($STARTINGLOCATION = "Citadel")
	send "q szh* l "&$PLANET&"* c "
else
	send "szh* "
end
settextlinetrigger CHECKFORHOLO :CONTINUECHECKHOLO "Select (H)olo Scan or (D)ensity Scan or (Q)uit?"
settextlinetrigger CHECKFORDENS :PHOTONEDHTORP "Relative Density Scan"
pause

:CONTINUECHECKHOLO
settexttrigger HTORPSECTOR :CONTINUEHTORPSECTOR "["&$CURRENT_SECTOR&"]"
pause

:CONTINUEHTORPSECTOR
if ($PHOTONS <= 0)
	echo ANSI_14&"*No Photons on hand.**"&ANSI_7
	goto :WAIT_FOR_COMMAND
end
setvar $I 1
while (SECTOR.WARPS[$CURRENT_SECTOR][$I] > 0)
	setvar $ADJ_SEC SECTOR.WARPS[$CURRENT_SECTOR][$I]
	if (SECTOR.TRADERCOUNT[$ADJ_SEC] > 0)
		setvar $TARGETINSECTOR FALSE
		setvar $CORPMEMBERINSECTOR FALSE
		setvar $J 1
		while (SECTOR.TRADERS[$ADJ_SEC][$J] <> 0)
			setvar $TEMPTARGET SECTOR.TRADERS[$ADJ_SEC][$J]
			getlength $TEMPTARGET $TARGETLENGTH
			if ($TARGETLENGTH >= 4)
				cuttext $TEMPTARGET $TARGETCORP ($TARGETLENGTH - 4) 999
				gettext $TARGETCORP $TARGETCORP "[" "]"
				if ($TARGETCORP <> $CORP)
					setvar $TARGETINSECTOR TRUE
				end
				if ($TARGETCORP = $CORP)
					setvar $CORPMEMBERINSECTOR TRUE
				end
			end
			add $J 1
		end
		if (($TARGETINSECTOR = TRUE) and ($CORPMEMBERINSECTOR = FALSE))
			send "c p y " $ADJ_SEC "* *q"
			send "'{" $BOT_NAME "} - Photon fired into sector "&$ADJ_SEC&"!*"
			goto :WAIT_FOR_COMMAND
		end
	end
	add $I 1
end
if ($STARTINGLOCATION = "Citadel")
	settexttrigger WAITFORCIT :CONTINUEWAITFORCIT "Citadel command (?=help)"
	pause

	:CONTINUEWAITFORCIT
end
echo ANSI_14&"*No valid targets**"&ANSI_7
goto :WAIT_FOR_COMMAND

:PHOTONEDHTORP
send "'{" $BOT_NAME "} - You have no holographic scanner, perhaps you were photoned?*"
goto :WAIT_FOR_COMMAND

:MOW

:M
gosub :DO_MOW
if (($CURRENT_PROMPT = "<StarDock>") or ($CURRENT_PROMPT = "<Hardware"))
	send "'{" $BOT_NAME "} - Safely on Stardock*"
end
if ($CURRENT_SECTOR <> $DESTINATION)
	send "'{" $BOT_NAME "} - Mow did not reach destination!*"
else
	send "'{" $BOT_NAME "} - Mow completed.*"
end
goto :WAIT_FOR_COMMAND

:GETCOURSE
setvar $SECTORS ""
settextlinetrigger SECTORSNOGO :SECTORSNOGO "Error - No route within"
settextlinetrigger SECTORLINETRIG :SECTORSLINE " > "
send "^f*"&$DESTINATION&"*q"
pause

:SECTORSNOGO
killtrigger SECTORLINETRIG
send "n * q"
send "'Clear Voids and try again!*"
goto :NOPATH
pause

:SECTORSLINE
killtrigger SECTORLINETRIG
killtrigger SECTORLINETRIG2
killtrigger SECTORLINETRIG3
killtrigger SECTORLINETRIG4
killtrigger DONEPATH
killtrigger DONEPATH2
setvar $LINE CURRENTLINE
replacetext $LINE ">" " "
striptext $LINE "("
striptext $LINE ")"
setvar $LINE $LINE&" "
getwordpos $LINE $POS "So what's the point?"
getwordpos $LINE $POS2 ": ENDINTERROG"
if (($POS > 0) or ($POS2 > 0))
	goto :NOPATH
end
getwordpos $LINE $POS " sector "
getwordpos $LINE $POS2 "TO"
if (($POS <= 0) and ($POS2 <= 0))
	setvar $SECTORS $SECTORS&" "&$LINE
end
getwordpos $LINE $POS " "&$DESTINATION&" "
getwordpos $LINE $POS2 "("&$DESTINATION&")"
getwordpos $LINE $POS3 "TO"
if ((($POS > 0) or ($POS2 > 0)) and ($POS3 <= 0))
	goto :GOTSECTORS
else
	settextlinetrigger SECTORLINETRIG :SECTORSLINE " > "
	settextlinetrigger SECTORLINETRIG2 :SECTORSLINE " "&$DESTINATION&" "
	settextlinetrigger SECTORLINETRIG3 :SECTORSLINE " "&$DESTINATION
	settextlinetrigger SECTORLINETRIG4 :SECTORSLINE "("&$DESTINATION&")"
	settextlinetrigger DONEPATH :SECTORSLINE "So what's the point?"
	settextlinetrigger DONEPATH2 :SECTORSLINE ": ENDINTERROG"
end
pause

:GOTSECTORS
setvar $SECTORS $SECTORS&" :::"
setvar $COURSELENGTH 0
setvar $INDEX 1

:KEEPGOING
getword $SECTORS $MOWCOURSE[$INDEX] $INDEX
while ($MOWCOURSE[$INDEX] <> ":::")
	add $COURSELENGTH 1
	add $INDEX 1
	getword $SECTORS $MOWCOURSE[$INDEX] $INDEX
end
return

:NOPATH
send "'{" $BOT_NAME "} - No path to that sector, cannot mow!*"
goto :WAIT_FOR_COMMAND

:DO_MOW
setarray $MOWCOURSE 80
setvar $CALLED FALSE
gosub :QUIKSTATS
setvar $STARTINGLOCATION $CURRENT_PROMPT
setvar $VALIDPROMPTS "Command <Underground> Do How Corporate Citadel Planet Computer Terra <StarDock> <FedPolice> <Tavern> <Libram <Galactic <Hardware <Shipyards>"
gosub :CHECKSTARTINGPROMPT
if ($STARTINGLOCATION = "Citadel")
	send "q"
	gosub :GETPLANETINFO
	send "c "
end
if ($STARTINGLOCATION = "Command")
	gosub :GETSHIPSTATS
elseif ($SHIP_MAX_ATTACK <= 0)
	setvar $SHIP_MAX_ATTACK 99991111
end
setvar $DESTINATION $PARM1
isnumber $NUMBER $DESTINATION
if ($NUMBER <> 1)
	send "'{" $BOT_NAME "} - Sector entered is not a number, cannot mow!*"
	goto :WAIT_FOR_COMMAND
elseif (($DESTINATION <= 0) or ($DESTINATION > SECTORS))
	send "'{" $BOT_NAME "} - Sector entered is not valid, cannot mow!*"
	goto :WAIT_FOR_COMMAND
end
setvar $DESTINATION ($PARM1 + 0)
getwordpos " "&$USER_COMMAND_LINE&" " $POS "kill"
if ($POS > 0)
	setvar $MOW_KILL TRUE
else
	setvar $MOW_KILL FALSE
end
getwordpos " "&$USER_COMMAND_LINE&" " $POS "saveme"
if ($POS > 0)
	setvar $MOW_SAVEME TRUE
else
	setvar $MOW_SAVEME FALSE
end
getwordpos " "&$USER_COMMAND_LINE&" " $POS " p "
if ($POS > 0)
	setvar $ARE_WE_DOCKING TRUE
else
	setvar $ARE_WE_DOCKING FALSE
end
setvar $FIGSTODROP $PARM2
isnumber $NUMBER $FIGSTODROP
if ($NUMBER <> TRUE)
	setvar $FIGSTODROP 0
else
	if ($FIGSTODROP > 50000)
		send "'{" $BOT_NAME "} - Cannot drop more than 50,000 fighters per sector!*"
		goto :WAIT_FOR_COMMAND
	elseif ($FIGSTODROP > $FIGHTERS)
		send "'{" $BOT_NAME "} - Fighters to drop cannot exceed total ship fighters.*"
		goto :WAIT_FOR_COMMAND
	end
end
if ($SHIP_MAX_ATTACK > $FIGHTERS)
	setvar $SHIP_MAX_ATTACK 9999
end
gosub :GETCOURSE
setvar $J 2
setvar $RESULT "q q q * "
while ($J <= $COURSELENGTH)
	setvar $RESULT $RESULT&"m  "&$MOWCOURSE[$J]&"*   "
	if (($MOWCOURSE[$J] > 10) and ($MOWCOURSE[$J] <> $STARDOCK))
		setvar $RESULT $RESULT&"za  "&$SHIP_MAX_ATTACK&"* *  "
	end
	if (($FIGSTODROP > 0) and (($MOWCOURSE[$J] > 10) and (($MOWCOURSE[$J] <> $STARDOCK) and ($J > 2))))
		setvar $RESULT $RESULT&"f "&$FIGSTODROP&" * c d "
		setvar $TARGET $MOWCOURSE[$J]
		gosub :ADDFIGTODATA
	end
	if (($J >= $COURSELENGTH) and (($MOW_SAVEME = TRUE) and ($FIGSTODROP = 0)))
		setvar $RESULT $RESULT&"f 1 * c d "
		setvar $TARGET $MOWCOURSE[$J]
		gosub :ADDFIGTODATA
	end
	if (($CALLED = FALSE) and (($MOW_SAVEME = TRUE) and ($J >= ($COURSELENGTH - 2))))
		setvar $RESULT $RESULT&"'"&$DESTINATION&"=saveme*  "
		setvar $CALLED TRUE
	end
	add $J 1
end
setvar $DOCKING_INSTRUCTIONS ""
if ($ARE_WE_DOCKING)
	setvar $DOCKING_INSTRUCTIONS " p z t *"
	if ($DESTINATION = $STARDOCK)
		setvar $DOCKING_INSTRUCTIONS " p z s g y g q h *"
	end
	setvar $RESULT $RESULT&$DOCKING_INSTRUCTIONS
elseif (($MOW_SAVEME = TRUE) and ($STARTINGLOCATION = "Citadel"))
	setvar $I 0
	while ($I < 8)
		add $I 1
		setvar $RESULT $RESULT&"l j"&#8&$PLANET&"*  *  j  c  *  *  "
	end
end
send $RESULT
gosub :QUIKSTATS
if (($CURRENT_PROMPT = "Command") and ($MOW_KILL = TRUE))
	setvar $STARTINGLOCATION "Command"
	gosub :GETSECTORDATA
	gosub :FASTATTACK
elseif ($CURRENT_PROMPT = "Planet")
	send "m * * * c "
	if ($MOW_KILL = FALSE)
		send "s* "
	else
		setvar $STARTINGLOCATION "Citadel"
		gosub :SCANIT_CIT_KILL
	end
elseif ($ARE_WE_DOCKING = FALSE)
	send "*"
end
return

:SAFEMOW

:SMOW
gosub :KILLTHETRIGGERS
gosub :QUIKSTATS
if ($SCAN_TYPE = "None")
	send "'{" $BOT_NAME "} - Safe Mow can only be run when you have a long range scanner.*"
	goto :WAIT_FOR_COMMAND
end
setvar $STARTINGLOCATION $CURRENT_PROMPT
setvar $VALIDPROMPTS "Command <Underground> Do How Corporate Citadel Planet Computer Terra <StarDock> <FedPolice> <Tavern> <Libram <Galactic <Hardware <Shipyards>"
gosub :CHECKSTARTINGPROMPT
if ($STARTINGLOCATION = "Command")
	gosub :GETSHIPSTATS
elseif ($SHIP_MAX_ATTACK <= 0)
	setvar $SHIP_MAX_ATTACK 99991111
end
setvar $DESTINATION $PARM1
isnumber $NUMBER $DESTINATION
if ($NUMBER <> 1)
	send "'{" $BOT_NAME "} - Sector entered is not a number, cannot mow!*"
	goto :WAIT_FOR_COMMAND
elseif (($DESTINATION <= 0) or ($DESTINATION > SECTORS))
	send "'{" $BOT_NAME "} - Sector entered is not valid, cannot mow!*"
	goto :WAIT_FOR_COMMAND
end
if ($PARM2 = "p")
	setvar $ARE_WE_DOCKING TRUE
else
	if ($PARM3 = "p")
		setvar $ARE_WE_DOCKING TRUE
	else
		setvar $ARE_WE_DOCKING FALSE
	end
end
setvar $FIGSTODROP $PARM2
isnumber $NUMBER $FIGSTODROP
if ($NUMBER <> 1)
	if ($PARM2 <> "p")
		send "'{" $BOT_NAME "} - Fighters to drop entered is not a number, cannot mow!*"
		goto :WAIT_FOR_COMMAND
	end
	setvar $FIGSTODROP 0
elseif ($FIGSTODROP > 50000)
	send "'{" $BOT_NAME "} - Cannot drop more than 50,000 fighters per sector!*"
	goto :WAIT_FOR_COMMAND
end
if ($SHIP_MAX_ATTACK > $FIGHTERS)
	setvar $SHIP_MAX_ATTACK 9999
end
gosub :GETCOURSE
setvar $J 3
setvar $RESULT "q q q * "
setvar $ISSAFE TRUE
while (($J <= $COURSELENGTH) and $ISSAFE)
	setvar $NEXTSAFESECTOR $MOWCOURSE[$J]
	if ($SCAN_TYPE = "Holo")
		send "sdsh"
	elseif ($SCAN_TYPE = "Dens")
		send "sd"
	end
	gosub :QUIKSTATS
	setvar $MINESSAFE (SECTOR.MINES.QUANTITY[$NEXTSAFESECTOR] <= 0) or (SECTOR.MINES.OWNER[$NEXTSAFESECTOR] = "yours") or (SECTOR.MINES.OWNER[$NEXTSAFESECTOR] = "belong to your Corp")
	setvar $FIGSSAFE (SECTOR.FIGS.QUANTITY[$NEXTSAFESECTOR] <= 0) or (SECTOR.FIGS.OWNER[$NEXTSAFESECTOR] = "yours") or (SECTOR.FIGS.OWNER[$NEXTSAFESECTOR] = "belong to your Corp")
	setvar $PLANETSAFE (SECTOR.PLANETCOUNT[$NEXTSAFESECTOR] <= 0) or ($NEXTSAFESECTOR = $STARDOCK) or ($NEXTSAFESECTOR <= 10)
	setvar $NAVHAZSAFE (SECTOR.NAVHAZ[$NEXTSAFESECTOR] <= 0)
	setvar $DENSITYSAFE (SECTOR.DENSITY[$NEXTSAFESECTOR] <= 0)
	setvar $LIMPETSSAFE (SECTOR.ANOMOLY[$NEXTSAFESECTOR] = FALSE) or (SECTOR.LIMPETS.OWNER[$NEXTSAFESECTOR] = "yours") or (SECTOR.LIMPETS.OWNER[$NEXTSAFESECTOR] = "belong to your Corp")
	if ($DENSITYSAFE or ($LIMPETSSAFE and ($FIGSSAFE and ($MINESSAFE and ($NAVHAZSAFE and $PLANETSAFE)))))
		send "m "&$MOWCOURSE[$J]&"* "
	else
		send "'{" $BOT_NAME "} - Cannot safely move into sector "&$NEXTSAFESECTOR&"*"
		goto :WAIT_FOR_COMMAND
	end
	if (($FIGSTODROP > 0) and (($MOWCOURSE[$J] > 10) and (($MOWCOURSE[$J] <> STARDOCK) and ($J > 2))))
		send "f "&$FIGSTODROP&" * c d "
		setvar $TARGET $MOWCOURSE[$J]
		gosub :ADDFIGTODATA
	end
	add $J 1
end
setvar $DOCKING_INSTRUCTIONS ""
if ($ARE_WE_DOCKING)
	setvar $DOCKING_INSTRUCTIONS " p z t *"
	if ($DESTINATION = $STARDOCK)
		setvar $DOCKING_INSTRUCTIONS " p z s g y g q h *"
	end
	send $DOCKING_INSTRUCTIONS
end
gosub :QUIKSTATS
if ($CURRENT_SECTOR <> $DESTINATION)
	send "'{" $BOT_NAME "} - Safe mow did not reach destination!*"
else
	send "'{" $BOT_NAME "} - Safe mow completed.*"
end
goto :WAIT_FOR_COMMAND

:LAND

:L
gosub :QUIKSTATS
setvar $STARTINGLOCATION $CURRENT_PROMPT
setvar $VALIDPROMPTS "Command"
gosub :CHECKSTARTINGPROMPT
isnumber $NUMBER $PARM1
if ($NUMBER = TRUE)
	if (($PARM1 = 0) and ($PLANET = 0))
		send "'{" $BOT_NAME "} - Incorrect Planet number*"
		goto :WAIT_FOR_COMMAND
	elseif ($PARM1 > 0)
		setvar $PLANET $PARM1
	else
	end
else
	send "'{" $BOT_NAME "} - Planet number entered is not a number*"
	goto :WAIT_FOR_COMMAND
end
gosub :LANDINGSUB
if ($SUCESSFULCITADEL)
	send "'{" $BOT_NAME "} - In Cit - Planet " $PLANET "*"
elseif ($SUCESSFULPLANET)
	send "'{" $BOT_NAME "} - At Planet Prompt - No Cit*"
end
goto :WAIT_FOR_COMMAND

:MAC
setvar $NMAC 1
goto :GO_MACRO

:NMAC
setvar $NMAC $PARM1

:GO_MACRO
isnumber $NUMBER $NMAC
if ($NUMBER <> TRUE)
	send "'{" $BOT_NAME "} - Invalid Macro Count*"
	goto :WAIT_FOR_COMMAND
end
if ($NMAC <= 0)
	send "'{" $BOT_NAME "} - Invalid Macro Count*"
	goto :WAIT_FOR_COMMAND
end
gosub :MACROPROTECTIONS
setvar $I 0
while ($I < $NMAC)
	send $USER_COMMAND_LINE
	add $I 1
end
if ($NMAC > 1)
	send "'{" $BOT_NAME "} - Numbered Macro - " $NMAC " Cycles Complete*"
else
	send "'{" $BOT_NAME "} - Macro Complete*"
end
goto :WAIT_FOR_COMMAND

:MACROPROTECTIONS
striptext $USER_COMMAND_LINE $BOT_NAME
striptext $USER_COMMAND_LINE " mac "
replacetext $USER_COMMAND_LINE "^m" "*"
replacetext $USER_COMMAND_LINE #42 "*"
getwordpos $USER_COMMAND_LINE $POS "`"
getwordpos $USER_COMMAND_LINE $POS2 "'"
getwordpos $USER_COMMAND_LINE $POS3 "="
if (($POS > 0) or ($POS2 > 0) or ($POS3 > 0))
	send "'{" $BOT_NAME "} - No talking with the bot :P*"
	goto :WAIT_FOR_COMMAND
end
setvar $CBYCHECK $USER_COMMAND_LINE
lowercase $CBYCHECK
getwordpos $CBYCHECK $POSC "c"
getwordpos $CBYCHECK $POSB "b"
getwordpos $CBYCHECK $POSY "y"
gosub :CURRENT_PROMPT
if (($CURRENT_PROMPT = "Computer") and (($POSB > 0) and ($POSY > 0)))
	send "'{" $BOT_NAME "} - Self Destruct Protection Activated*"
	goto :WAIT_FOR_COMMAND
end
if (($CURRENT_PROMPT = "����������") and ($POSY > 0))
	send "'{" $BOT_NAME "} - Self Destruct Protection Activated*"
	goto :WAIT_FOR_COMMAND
end
getlength $CBYCHECK $LENGTH
setvar $I 1
while ($I <= $LENGTH)
	if (($POSC > 0) and (($POSB > $POSC) and ($POSY > $POSB)))
		send "'{" $BOT_NAME "} - Self Destruct Protection Activated*"
		goto :WAIT_FOR_COMMAND
	end
	if ($FOUNDC = FALSE)
		getwordpos $CBYCHECK $POS "c"
		if ($POS = 1)
			setvar $FOUNDC TRUE
		end
	elseif ($FOUNDB = FALSE)
		getwordpos $CBYCHECK $POS "b"
		if ($POS = 1)
			setvar $FOUNDB TRUE
		end
	elseif ($FOUNDY = FALSE)
		getwordpos $CBYCHECK $POS "y"
		if ($POS = 1)
			setvar $FOUNDY TRUE
		end
	end
	if ($FOUNDC and ($FOUNDB and $FOUNDY))
		send "'{" $BOT_NAME "} - Self Destruct Protection Activated*"
		goto :WAIT_FOR_COMMAND
	end
	if ($TESTLENGTH > 1)
		cuttext $CBYCHECK $CBYCHECK 2 9999
	end
	add $I 1
end
return

:STORESHIP

:SHIPSTORE
setvar $SHIPCOUNTER 1

:_READSHIPLIST
read $CAP_FILE $SHIPINF $SHIPCOUNTER
if ($SHIPINF <> EOF)
	getword $SHIPINF $SHIELDS 1
	getlength $SHIELDS $SHIELDLEN
	getword $SHIPINF $DEFODD 2
	getlength $DEFODD $DEFODDLEN
	getword $SHIPINF $OFF_ODDS 3
	getlength $OFF_ODDS $FILLER1LEN
	getword $SHIPINF $SHIP_COST 4
	getlength $SHIP_COST $FILLER2LEN
	getword $SHIPINF $MAX_HOLDS 5
	getlength $MAX_HOLDS $FILLER3LEN
	getword $SHIPINF $MAX_FIGHTERS 6
	getlength $MAX_FIGHTERS $FILLER4LEN
	getword $SHIPINF $INIT_HOLDS 7
	getlength $INIT_HOLDS $FILLER5LEN
	getword $SHIPINF $TPW 8
	getlength $TPW $FILLER6LEN
	setvar $STARTLEN ($SHIELDLEN + ($DEFODDLEN + ($FILLER1LEN + ($FILLER2LEN + ($FILLER3LEN + ($FILLER4LEN + ($FILLER5LEN + ($FILLER6LEN + 9))))))))
	cuttext $SHIPINF $SHIPNAME $STARTLEN 999
	setvar $DATABASE $DATABASE&"^^^^^^"&$SHIPNAME&"^^^^^^"
	add $SHIPCOUNTER 1
	goto :_READSHIPLIST
end
gosub :CURRENT_PROMPT
setvar $STARTINGLOCATION $CURRENT_PROMPT
setvar $VALIDPROMPTS "Command Citadel"
gosub :CHECKSTARTINGPROMPT
send "c"
waiton "Computer command"
send ";"

:_KEEPLOOKINGSHIPNAME
gosub :KILLTHETRIGGERS
settextlinetrigger CHECKINGFORSHIPNAME :_CHECKSHIPNAME
pause

:_CHECKSHIPNAME
if (CURRENTLINE = "")
	goto :_KEEPLOOKINGSHIPNAME
else
	setvar $CURRENT_LINE CURRENTLINE
	getword $CURRENT_LINE $TEMP 1
	cuttext $TEMP $FRONTLETTER 1 1
	gettext $CURRENT_LINE $SHIP_NAME $FRONTLETTER "          "
	setvar $SHIP_NAME $FRONTLETTER&$SHIP_NAME
	getwordpos $DATABASE $POS "^^^^^^"&$SHIP_NAME&"^^^^^^"
	if ($POS > 0)
		send "'{" $BOT_NAME "} - This ship is already stored in bot file.*"
		goto :WAIT_FOR_COMMAND
	end
end

:_SN
settextlinetrigger HC :_HC "Basic Hold Cost:"
pause

:_HC
setvar $LINE CURRENTLINE
striptext $LINE "Basic Hold Cost:"
striptext $LINE "Initial Holds:"
striptext $LINE "Maximum Shields:"
getword $LINE $INIT_HOLDS 2
getword $LINE $MAX_SHIELDS 3
striptext $MAX_SHIELDS ","
settextlinetrigger OO :_OO2 "Offensive Odds:"
pause

:_OO2
setvar $LINE CURRENTLINE
striptext $LINE "Main Drive Cost:"
striptext $LINE "Max Fighters:"
striptext $LINE "Offensive Odds:"
getword $LINE $MAX_FIGS 2
getword $LINE $OFF_ODDS 3
striptext $MAX_FIGS ","
striptext $OFF_ODDS ":1"
striptext $OFF_ODDS "."
settextlinetrigger DO :_DO "Defensive Odds:"
pause

:_DO
setvar $LINE CURRENTLINE
striptext $LINE "Computer Cost:"
striptext $LINE "Turns Per Warp:"
striptext $LINE "Defensive Odds:"
getword $LINE $DEF_ODDS 3
striptext $DEF_ODDS ":1"
striptext $DEF_ODDS "."
getword $LINE $TPW 2
settextlinetrigger SC :_SC "Ship Base Cost:"
pause

:_SC
setvar $LINE CURRENTLINE
striptext $LINE "Ship Base Cost:"
getword $LINE $COST 1
striptext $COST ","
getlength $COST $COSTLEN
if ($COSTLEN = 7)
	add $COST 10000000
end
settextlinetrigger MH :_MH "Maximum Holds:"
pause

:_MH
setvar $LINE CURRENTLINE
striptext $LINE "Maximum Holds:"
getword $LINE $MAX_HOLDS 1
setvar $ISDEFENDER FALSE
write $CAP_FILE $MAX_SHIELDS&" "&$DEF_ODDS&" "&$OFF_ODDS&" "&$COST&" "&$MAX_HOLDS&" "&$MAX_FIGS&" "&$INIT_HOLDS&" "&$TPW&" "&$ISDEFENDER&" "&$SHIP_NAME
send "'{" $BOT_NAME "} - "&$SHIP_NAME&" added to bot's ship file.*"
send "q"
gosub :LOADSHIPINFO
goto :WAIT_FOR_COMMAND

:QSS
setarray $SPACE 27
setarray $H 27
setarray $QSS 27
setvar $SPACE[1] 18
setvar $SPACE[2] 18
setvar $SPACE[3] 18
setvar $SPACE[4] 14
setvar $SPACE[5] 14
setvar $SPACE[6] 10
setvar $SPACE[7] 10
setvar $SPACE[8] 10
setvar $SPACE[9] 10
setvar $SPACE[10] 10
setvar $SPACE[11] 14
setvar $SPACE[12] 12
setvar $SPACE[13] 12
setvar $SPACE[14] 12
setvar $SPACE[15] 11
setvar $SPACE[16] 9
setvar $SPACE[17] 12
setvar $SPACE[18] 12
setvar $SPACE[19] 14
setvar $SPACE[20] 11
setvar $SPACE[21] 14
setvar $SPACE[22] 11
setvar $SPACE[23] 11
setvar $SPACE[24] 11
setvar $SPACE[25] 18
setvar $SPACE[26] 18
setvar $SPACE[27] 5
setvar $H[1] "Sect"
setvar $H[2] "Turns"
setvar $H[3] "Creds"
setvar $H[4] "Figs"
setvar $H[5] "Shlds"
setvar $H[6] "Hlds"
setvar $H[7] "Ore"
setvar $H[8] "Org"
setvar $H[9] "Equ"
setvar $H[10] "Col"
setvar $H[11] "Phot"
setvar $H[12] "Armd"
setvar $H[13] "Lmpt"
setvar $H[14] "GTorp"
setvar $H[15] "TWarp"
setvar $H[16] "Clks"
setvar $H[17] "Beacns"
setvar $H[18] "AtmDt"
setvar $H[19] "Crbo"
setvar $H[20] "EPrb"
setvar $H[21] "MDis"
setvar $H[22] "PsPrb"
setvar $H[23] "PlScn"
setvar $H[24] "LRS"
setvar $H[25] "Aln"
setvar $H[26] "Exp"
setvar $H[27] "Ship"
gosub :QUIKSTATS
setvar $QSS[1] $CURRENT_SECTOR
if ($UNLIMITEDGAME)
	setvar $QSS[2] "Unlimited"
else
	setvar $QSS[2] $TURNS
end
setvar $QSS[3] $CREDITS
setvar $QSS[4] $FIGHTERS
setvar $QSS[5] $SHIELDS
setvar $QSS[6] $TOTAL_HOLDS
setvar $QSS[7] $ORE_HOLDS
setvar $QSS[8] $ORGANIC_HOLDS
setvar $QSS[9] $EQUIPMENT_HOLDS
setvar $QSS[10] $COLONIST_HOLDS
setvar $QSS[11] $PHOTONS
setvar $QSS[12] $ARMIDS
setvar $QSS[13] $LIMPETS
setvar $QSS[14] $GENESIS
setvar $QSS[15] $TWARP_TYPE
setvar $QSS[16] $CLOAKS
setvar $QSS[17] $BEACONS
setvar $QSS[18] $ATOMIC
setvar $QSS[19] $CORBO
setvar $QSS[20] $EPROBES
setvar $QSS[21] $MINE_DISRUPTORS
setvar $QSS[22] $PSYCHIC_PROBE
setvar $QSS[23] $PLANET_SCANNER
setvar $QSS[24] $SCAN_TYPE
setvar $QSS[25] $ALIGNMENT
setvar $QSS[26] $EXPERIENCE
setvar $QSS[27] $SHIP_NUMBER
setvar $QSS_SS 0
setvar $QSS_COUNT 1
setvar $SPC " "
setvar $OVERALL 15

:QSS_GATHER
while ($QSS_COUNT <= 27)
	setvar $SPC_COUNT 1
	uppercase $H[$QSS_COUNT]
	setvar $QSS_VAR[$QSS_COUNT] $H[$QSS_COUNT]&" = "&$QSS[$QSS_COUNT]
	getlength $QSS_VAR[$QSS_COUNT] $LENGTH
	subtract $SPACE[$QSS_COUNT] $LENGTH
	while ($SPC_COUNT <= $SPACE[$QSS_COUNT])
		mergetext $QSS_VAR[$QSS_COUNT] $SPC $QSS_VAR[$QSS_COUNT]
		add $SPC_COUNT 1
	end
	add $QSS_COUNT 1
end

:QSS_SEND
send "'*"
send $QSS_VAR[1]&"|"&$QSS_VAR[6]&"|"&$QSS_VAR[4]&"|"&$QSS_VAR[12]&"|"&$QSS_VAR[15]&"*"
send $QSS_VAR[2]&"|"&$QSS_VAR[7]&"|"&$QSS_VAR[5]&"|"&$QSS_VAR[13]&"|"&$QSS_VAR[23]&"*"
send $QSS_VAR[3]&"|"&$QSS_VAR[8]&"|"&$QSS_VAR[11]&"|"&$QSS_VAR[14]&"|"&$QSS_VAR[24]&"*"
send $QSS_VAR[25]&"|"&$QSS_VAR[9]&"|"&$QSS_VAR[19]&"|"&$QSS_VAR[18]&"|"&$QSS_VAR[22]&"*"
send $QSS_VAR[26]&"|"&$QSS_VAR[10]&"|"&$QSS_VAR[21]&"|"&$QSS_VAR[17]&"|"&$QSS_VAR[20]&"*"
send $QSS_VAR[27]&"**"
goto :WAIT_FOR_COMMAND

:PWARP

:P
gosub :KILLTHETRIGGERS
if ($PARM1 <> $CURRENT_SECTOR)
	gosub :CURRENT_PROMPT
else
	gosub :QUIKSTATS
end
setvar $STARTINGLOCATION $CURRENT_PROMPT
setvar $VALIDPROMPTS "Citadel"
gosub :CHECKSTARTINGPROMPT
isnumber $TEST $PARM1
if (($TEST = FALSE) or ($PARM1 = 0))
	send "'{" $BOT_NAME "} - Sector must be entered as a number between 11-" SECTORS "*"
	goto :WAIT_FOR_COMMAND
else
	setvar $WARPTO $PARM1
	if ($CURRENT_SECTOR = $WARPTO)
		send "'{" $BOT_NAME "} - Already in that sector!*"
		goto :WAIT_FOR_COMMAND
	end
end
gosub :PWARPTO
goto :WAIT_FOR_COMMAND

:PWARPTO
send "p" $WARPTO "*y"
settextlinetrigger PWARP_LOCK :PWARP_LOCK "Locating beam pinpointed"
settextlinetrigger NO_PWARP_LOCK :NO_PWARP_LOCK "Your own fighters must be"
settextlinetrigger ALREADY :ALREADY "You are already in that sector!"
settextlinetrigger NO_ORE :NO_ORE "You do not have enough Fuel Ore"
settextlinetrigger NO_PWARP :NOPWARP "This Citadel does not have a Planetary TransWarp"
pause

:NOPWARP
gosub :KILLTHETRIGGERS
send "'{" $BOT_NAME "} - Planet Does Not Have A Planetary TransWarp Drive!*"
return

:NO_PWARP_LOCK
gosub :KILLTHETRIGGERS
setvar $TARGET $WARPTO
gosub :REMOVEFIGFROMDATA
send "'{" $BOT_NAME "} - No fighter down at that location!*"
return

:NO_ORE
gosub :KILLTHETRIGGERS
send "'{" $BOT_NAME "} - Not enough fuel for that pwarp.*"
return

:PWARP_LOCK
gosub :KILLTHETRIGGERS
waiton "Planet is now in sector"
send "'{" $BOT_NAME "} - Planet moved to sector "&$WARPTO&".*"
setvar $TARGET $WARPTO
gosub :ADDFIGTODATA
return

:ALREADY
gosub :KILLTHETRIGGERS
send "'{" $BOT_NAME "} - Planet already in that sector!.*"
return

:BWARP

:B
gosub :KILLTHETRIGGERS
if ($PARM1 <> $CURRENT_SECTOR)
	gosub :CURRENT_PROMPT
else
	gosub :QUIKSTATS
end
setvar $STARTINGLOCATION $CURRENT_PROMPT
setvar $VALIDPROMPTS "Citadel"
gosub :CHECKSTARTINGPROMPT
gosub :TRAVELPROTECTIONS
send "b"
settexttrigger NOBWARP :NOBWARP "Would you like to place a subspace order for one? "
settexttrigger YESBWARP :YESBWARP "Beam to what sector? (U="
settexttrigger IGBWARP :BWARPPHOTONED "Your ship was hit by a Photon and has been disabled"
pause

:NOBWARP
gosub :KILLTHETRIGGERS
send "*"
setvar $MESSAGE "{"&$BOT_NAME&"} - No Bwarp installed on this planet*"
gosub :SWITCHBOARD
goto :WAIT_FOR_COMMAND

:YESBWARP
gosub :KILLTHETRIGGERS
send $WARPTO&"*"
settexttrigger BWARP_LOCK :BWARP_NO_RANGE "This planetary transporter does not have the range."
settexttrigger NO_BWRP_LOCK :NO_BWARP_LOCK "Do you want to make this transport blind?"
settexttrigger BWARP_READY :BWARP_LOCK "All Systems Ready, shall we engage?"
settextlinetrigger NO_BWARPFUEL :BWARPNOFUEL "This planet does not have enough Fuel Ore to transport you."
pause

:BWARP_NO_RANGE
gosub :KILLTHETRIGGERS
setvar $MESSAGE "{"&$BOT_NAME&"} - Not enough range on this planet's transporter.*"
gosub :SWITCHBOARD
goto :WAIT_FOR_COMMAND

:NO_BWARP_LOCK
gosub :KILLTHETRIGGERS
send "* "
setvar $TARGET $WARPTO
gosub :REMOVEFIGFROMDATA
setvar $MESSAGE "{"&$BOT_NAME&"} - No fighter down at that destination, aborting*"
gosub :SWITCHBOARD
goto :WAIT_FOR_COMMAND

:BWARP_LOCK
gosub :KILLTHETRIGGERS
send "y     * "
send "'{" $BOT_NAME "} - B-warp completed.*"
setvar $TARGET $WARPTO
gosub :ADDFIGTODATA
goto :WAIT_FOR_COMMAND

:BWARPNOFUEL
gosub :KILLTHETRIGGERS
setvar $MESSAGE "{"&$BOT_NAME&"} - Not enough fuel on the planet to make the transport!*"
gosub :SWITCHBOARD
goto :WAIT_FOR_COMMAND

:BWARPPHOTONED
gosub :KILLTHETRIGGERS
setvar $MESSAGE "{"&$BOT_NAME&"} - I have been photoned and can not B-warp!*"
gosub :SWITCHBOARD
goto :WAIT_FOR_COMMAND

:TWARP

:T
gosub :KILLTHETRIGGERS
setvar $WARPTO_P ""
gosub :QUIKSTATS
setvar $STARTINGLOCATION $CURRENT_PROMPT
setvar $VALIDPROMPTS "Command <Underground> Do How Corporate Citadel Planet Computer Terra <StarDock> <FedPolice> <Tavern> <Libram <Galactic <Hardware <Shipyards>"
gosub :CHECKSTARTINGPROMPT
if ($TWARP_TYPE = "No")
	setvar $MESSAGE "{"&$BOT_NAME&"} - This ship does not have a transwarp drive!*"
	gosub :SWITCHBOARD
	goto :WAIT_FOR_COMMAND
end
gosub :TRAVELPROTECTIONS
gosub :TWARPTO
if ($TWARPSUCCESS = FALSE)
	if (($STARTINGLOCATION = "Citadel") or ($STARTINGLOCATION = "Planet"))
		if ($PLANET <> 0)
			gosub :CURRENT_PROMPT
			if ($CURRENT_PROMPT = "Command")
				gosub :LANDINGSUB
			end
		end
		goto :WAIT_FOR_COMMAND
	end
	if (($STARTINGLOCATION = "<StarDock>") or ($STARTINGLOCATION = "<FedPolice") or ($STARTINGLOCATION = "<Tavern>") or ($STARTINGLOCATION = "<Libram") or ($STARTINGLOCATION = "<Galact") or ($STARTINGLOCATION = "<Hardware") or ($STARTINGLOCATION = "<Shipyards>"))
		send "p z s h *"
		goto :WAIT_FOR_COMMAND
	end
	setvar $MESSAGE "{"&$BOT_NAME&"} - "&$MSG&"*"
	gosub :SWITCHBOARD
else
	if ($PARM2 = "p")
		send $WARPTO_P
	elseif (($WARPTO_P <> 0) and ($WARPTO_P <> ""))
		setvar $PLANET $WARPTO_P
		gosub :LANDINGSUB
	end
	send "'{"&$BOT_NAME&"} - "&$MSG&"*"
end
goto :WAIT_FOR_COMMAND

:TRAVELPROTECTIONS
isnumber $TEST $PARM1
if ($TEST = FALSE)
	setvar $MESSAGE "{"&$BOT_NAME&"} - Sector must be entered as a number*"
	gosub :SWITCHBOARD
	goto :WAIT_FOR_COMMAND
else
	if ($PARM2 = "p")
		setvar $WARPTO_P "p z t *"
		if ($PARM1 = $STARDOCK)
			setvar $WARPTO_P "p z s h *"
		end
	else
		isnumber $TEST $PARM2
		if ($TEST = FALSE)
			setvar $WARPTO_P ""
		else
			setvar $WARPTO_P $PARM2
		end
	end
	setvar $WARPTO $PARM1
	if ($CURRENT_SECTOR = $WARPTO)
		setvar $MESSAGE "{"&$BOT_NAME&"} - Already in that sector!*"
		gosub :SWITCHBOARD
		goto :WAIT_FOR_COMMAND
	elseif (($WARPTO <= 0) or ($WARPTO > SECTORS))
		setvar $MESSAGE "{"&$BOT_NAME&"} - Destination sector is out of range!*"
		gosub :SWITCHBOARD
		goto :WAIT_FOR_COMMAND
	end
end
return

:COURSE
gosub :KILLTHETRIGGERS
gosub :QUIKSTATS
isnumber $TEST $PARM1
if (($PARM1 = 0) or ($PARM1 = "") or ($TEST = FALSE))
	send "'{" $BOT_NAME "} - Sectors entered not valid.*"
	goto :WAIT_FOR_COMMAND
end
isnumber $TEST $PARM2
if (($TEST = FALSE) or ($PARM2 = 0))
	setvar $DESTINATION $PARM1
	setvar $START $CURRENT_SECTOR
else
	if ($PARM2 > 0)
		setvar $START $PARM1
		setvar $DESTINATION $PARM2
	else
		send "'{" $BOT_NAME "} - Sectors entered not valid.*"
		goto :WAIT_FOR_COMMAND
	end
end
send "^f"&$START&"*"&$DESTINATION&"*q "
waiton ": ENDINTERROG"
getcourse $COURSE $START $DESTINATION
setvar $I 1
setvar $DIRECTIONS ""
while ($I <= $COURSE)
	getsectorparameter $COURSE[$I] "FIGSEC" $ISFIGGED
	if ($ISFIGGED)
		setvar $DIRECTIONS $DIRECTIONS&"["&$COURSE[$I]&"]"
	else
		setvar $DIRECTIONS $DIRECTIONS&$COURSE[$I]
	end
	if ($I <> $COURSE)
		setvar $DIRECTIONS $DIRECTIONS&" > "
	end
	add $I 1
end
send "'{" $BOT_NAME "} - Path from "&$START&" to "&$DESTINATION&": "&$DIRECTIONS&"*"
goto :WAIT_FOR_COMMAND

:LOGOFF

:LOGOUT
killalltriggers
gosub :QUIKSTATS
setvar $STARTINGLOCATION $CURRENT_PROMPT
setvar $QUITTINGWITHNOTIMER FALSE
isnumber $TEST $PARM1
if (($TEST = FALSE) or ($PARM1 <= 0))
	setvar $QUITTINGWITHNOTIMER TRUE
else
	setvar $TIMETOLOGBACKIN ($PARM1 * 60)
	gosub :CALCTIME
end
setvar $CLOAKINGOUT FALSE
getwordpos " "&$USER_COMMAND_LINE&" " $POS " cloak "
if ($POS > 0)
	setvar $CLOAKINGOUT TRUE
end
if (($CLOAKINGOUT = TRUE) and ($CLOAKS > 0))
	if ($QUITTINGWITHNOTIMER)
		send "'{" $BOT_NAME "} - Logging and cloaking out until I am at keys to login again.*"
	else
		send "'{" $BOT_NAME "} - Logging and cloaking out for "&$HOURS&" hours, "&$MINUTES&" minutes, and "&$SECONDS&" seconds.*"
	end
	send "q q q q  * * * * q q q q y y x *"
	waiton "Enter your choice:"
else
	if ($QUITTINGWITHNOTIMER)
		send "'{" $BOT_NAME "} - Logging out until I am at keys to login again.*"
	else
		send "'{" $BOT_NAME "} - Logging out for "&$HOURS&" hours, "&$MINUTES&" minutes, and "&$SECONDS&" seconds.*"
	end
	if ($STARTINGLOCATION = "Citadel")
		send "ryy* x *##"
		waiton "Game Server"
	else
		send "q q q q  * * * * q q q q y*"
		waiton "Enter your choice:"
	end
end
disconnect
setvar $TIMER 0
if ($QUITTINGWITHNOTIMER)
	halt
end
settextouttrigger LOGEARLY :ENDLOGOFFGAME #32
while ($TIMETOLOGBACKIN > 0)
	gosub :CALCTIME
	echo ANSI_10 #27&"[1A"&#27&"[K"&$HOURS ":" $MINUTES ":" $SECONDS " left before entering game " GAME " (" GAMENAME ") "&ANSI_15&" ["&ANSI_14&"Spacebar to relog"&ANSI_15&"]*"
	setdelaytrigger TIMEBEFORERELOG :RELOGTIMER 1000
	pause

	:RELOGTIMER
	setvar $TIMETOLOGBACKIN ($TIMETOLOGBACKIN - 1)
end

:ENDLOGOFFGAME
killtrigger LOGEARLY
killtrigger TIMEBEFORERELOG
goto :RELOG_ATTEMPT

:CALCTIME
setvar $HOURS 0
setvar $MINUTES 0
setvar $SECONDS 0
setvar $TESTTIME $TIMETOLOGBACKIN
if ($TESTTIME >= 3600)
	setvar $HOURS ($TESTTIME / 3600)
	setvar $TESTTIME ($TESTTIME - ($HOURS * 3600))
end
if ($TESTTIME >= 60)
	setvar $MINUTES ($TESTTIME / 60)
	setvar $TESTTIME ($TESTTIME - ($MINUTES * 60))
end
if ($TESTTIME >= 1)
	setvar $SECONDS $TESTTIME
end
if ($HOURS < 10)
	setvar $HOURS 0&$HOURS
end
if ($MINUTES < 10)
	setvar $MINUTES 0&$MINUTES
end
if ($SECONDS < 10)
	setvar $SECONDS 0&$SECONDS
end
return

:PLIMP

:LIMP
setvar $LIMP "p"
goto :_LIMP

:CLIMP
setvar $LIMP "c"
goto :_LIMP

:_LIMP
gosub :MINEPROTECTIONS
if ($PARM1 > $LIMPETS)
	setvar $PARM1 $LIMPETS
end

:PLIMP
gosub :KILLTHETRIGGERS
if ($LIMPETS <= 0)
	send "'{" $BOT_NAME "} - Out of limpets!*"
	goto :WAIT_FOR_COMMAND
end
if ($STARTINGLOCATION = "Citadel")
	send "q q z* h2z" $PARM1 "* z " $LIMP " z * * *l " $PLANET "* c"
elseif ($STARTINGLOCATION = "Command")
	send "z* h2z" $PARM1 "* z " $LIMP " z * *"
end
settextlinetrigger TOOMANYPL :TOOMANY_LIMP "!  You are limited to "
settextlinetrigger PLCLEAR :PLCLEAR_LIMP "Done. You have "
settextlinetrigger ENEMYPL :NOPERDOWN_LIMP "These mines are not under your control."
settextlinetrigger NOTENOUGH :TOOMANY_LIMP "You don't have that many mines available."
pause

:PLCLEAR_LIMP
gosub :KILLTHETRIGGERS
setvar $ISLIMPED TRUE
if ($STARTINGLOCATION = "Citadel")
	waiton "Citadel command (?=help)"
	send "s* "
elseif ($STARTINGLOCATION = "Command")
	send "d* "
end
settextlinetrigger PERDOWN :PERDOWN_LIMP "(Type 2 Limpet) (yours)"
settextlinetrigger CORDOWN :CORDOWN_LIMP "(Type 2 Limpet) (belong to your Corp)"
settextlinetrigger NOPERDOWN :NOPERDOWN_LIMP "Warps to Sector(s) :"
pause

:CORDOWN_LIMP
gosub :KILLTHETRIGGERS
send "'{" $BOT_NAME "} - " $PARM1 " Corporate Limpets Deployed!*"
goto :DONE_LIMP

:PERDOWN_LIMP
gosub :KILLTHETRIGGERS
send "'{" $BOT_NAME "} - " $PARM1 " Personal Limpet Deployed!*"
goto :DONE_LIMP

:NOPERDOWN_LIMP
gosub :KILLTHETRIGGERS
send "'{" $BOT_NAME "} - Sector already has enemy limpets present!*"
setvar $ISLIMPED FALSE
goto :DONE_LIMP

:TOOMANY_LIMP
send "'{" $BOT_NAME "} - Cannot Deploy Limps!*"

:DONE_LIMP
if ($ISLIMPED)
	setsectorparameter $CURRENT_SECTOR "LIMPSEC" TRUE
else
	setsectorparameter $CURRENT_SECTOR "LIMPSEC" FALSE
end
gosub :KILLTHETRIGGERS
goto :WAIT_FOR_COMMAND

:MINEPROTECTIONS
gosub :KILLTHETRIGGERS
gosub :QUIKSTATS
setvar $STARTINGLOCATION $CURRENT_PROMPT
isnumber $TEST $PARM1
if (($TEST = FALSE) or ($PARM1 = 0))
	setvar $PARM1 1
end
setvar $VALIDPROMPTS "Command Citadel"
gosub :CHECKSTARTINGPROMPT
if ($STARTINGLOCATION = "Citadel")
	send "q"
	gosub :GETPLANETINFO
	send "c"
end
return

:PMINE
setvar $ARMID "p"
goto :_MINE

:CMINE

:MINE
setvar $ARMID "c"
goto :_MINE

:_MINE
gosub :MINEPROTECTIONS
if ($PARM1 > $ARMIDS)
	setvar $PARM1 $ARMIDS
end

:_CMINE
gosub :KILLTHETRIGGERS
if ($ARMIDS <= 0)
	send "'{" $BOT_NAME "} - Out of Armid Mines!*"
	goto :WAIT_FOR_COMMAND
end
if ($STARTINGLOCATION = "Citadel")
	send "q q z n h1 z " $PARM1 "*  z" $ARMID " z n n  *l " $PLANET "* c"
else
	send "z n h1 z " $PARM1 "*  z" $ARMID " z n"
end
settextlinetrigger TOOMANYPL :TOOMANY_MINE "!  You are limited to "
settextlinetrigger PLCLEAR :PLCLEAR_MINE "Done. You have "
settextlinetrigger ENEMYPL :NOPERDOWN_MINE "These mines are not under your control."
settextlinetrigger NOTENOUGH :TOOMANY_MINE "You don't have that many mines available."
pause

:PLCLEAR_MINE
gosub :KILLTHETRIGGERS
setvar $ISMINED TRUE
if ($STARTINGLOCATION = "Citadel")
	waiton "Citadel command (?=help)"
	send "s*"
else
	waiton "Command [TL="
	send "d*"
end
settextlinetrigger PERDOWN :PERDOWN_MINE "(Type 1 Armid) (yours)"
settextlinetrigger CORDOWN :CORDOWN_MINE "(Type 1 Armid) (belong to your Corp)"
settextlinetrigger NOPERDOWN :NOPERDOWN_MINE "Citadel treasury contains"
pause

:CORDOWN_MINE
send "'{" $BOT_NAME "} - " $PARM1 " Corporate Mines Deployed!*"
goto :DONE_ARMID

:PERDOWN_MINE
send "'{" $BOT_NAME "} - " $PARM1 " Personal Mines Deployed!*"
goto :DONE_ARMID

:NOPERDOWN_MINE
send "'{" $BOT_NAME "} - Sector already has enemy Armid Mines present!*"
setvar $ISMINED FALSE
goto :DONE_ARMID

:TOOMANY_MINE
send "'{" $BOT_NAME "} - Cannot Deploy Armid Mines!*"

:DONE_ARMID
if ($ISMINED)
	setsectorparameter $CURRENT_SECTOR "MINESEC" TRUE
else
	setsectorparameter $CURRENT_SECTOR "MINESEC" FALSE
end
goto :WAIT_FOR_COMMAND

:MINES
gosub :KILLTHETRIGGERS
gosub :QUIKSTATS
setvar $STARTINGLOCATION $CURRENT_PROMPT
getword $USER_COMMAND_LINE $PARM1 1 "NONE"
if ($PARM1 = "NONE")
	setvar $PARM1 3
end
setvar $VALIDPROMPTS "Command Citadel"
gosub :CHECKSTARTINGPROMPT
if ($STARTINGLOCATION = "Citadel")
	send "q "
	gosub :GETPLANETINFO
	send "c "
end
setvar $PREDEPLOYARMIDS $ARMIDS
setvar $PREDEPLOYLIMPETS $LIMPETS
if ($STARTINGLOCATION = "Citadel")
	send "s* "
	setvar $START_MAC "q q "
	setvar $END_MAC "l "&$PLANET&"* c "
else
	send "** "
	setvar $START_MAC ""
	setvar $END_MAC ""
end
waiton "Warps to Sector(s) :"
setvar $LIMPETOWNER SECTOR.LIMPETS.OWNER[$CURRENT_SECTOR]
setvar $ARMIDOWNER SECTOR.MINES.OWNER[$CURRENT_SECTOR]
if (($ARMIDS <= 0) and (($ARMIDOWNER <> "belong to your Corp") and ($ARMIDOWNER <> "yours")))
	send "'{" $BOT_NAME "} - Out of armids!*"
	goto :WAIT_FOR_COMMAND
elseif ($PARM1 > $ARMIDS)
	setvar $PARM1 $ARMIDS
end
if (($LIMPETS <= 0) and (($LIMPETOWNER <> "belong to your Corp") and ($LIMPETOWNER <> "yours")))
	send "'{" $BOT_NAME "} - Out of limpets!*"
	goto :WAIT_FOR_COMMAND
elseif ($PARM1 > $LIMPETS)
	setvar $PARM1 $LIMPETS
end
send $START_MAC "z n h 2 z " $PARM1 "*  zc * * h 1 z " $PARM1 "*  z c * * * " $END_MAC
gosub :QUIKSTATS
if (($PREDEPLOYARMIDS > $ARMIDS) and ($PREDEPLOYLIMPETS > $LIMPETS))
	send "'{" $BOT_NAME "} - " $PARM1 " Armid and Limpet mines deployed into the sector!*"
	setsectorparameter $CURRENT_SECTOR "LIMPSEC" TRUE
	setsectorparameter $CURRENT_SECTOR "MINESEC" TRUE
elseif ($PREDEPLOYARMIDS > $ARMIDS)
	send "'{" $BOT_NAME "} - " $PARM1 " Armid mine(s) deployed into the sector!*"
	setsectorparameter $CURRENT_SECTOR "MINESEC" TRUE
elseif ($PREDEPLOYLIMPETS > $LIMPETS)
	send "'{" $BOT_NAME "} - " $PARM1 " Limpet mine(s) deployed into the sector!*"
	setsectorparameter $CURRENT_SECTOR "LIMPSEC" TRUE
end
if ($PREDEPLOYARMIDS < $ARMIDS)
	send "'{" $BOT_NAME "} - " ($ARMIDS - $PREDEPLOYARMIDS) " Armid mines picked up from sector!*"
elseif (($PREDEPLOYARMIDS = $ARMIDS) and (($ARMIDOWNER <> "belong to your Corp") and ($ARMIDOWNER <> "yours")))
	send "'{" $BOT_NAME "} - Enemy armid(s) present in sector, cannot deploy!*"
end
if ($PREDEPLOYLIMPETS < $LIMPETS)
	send "'{" $BOT_NAME "} - " ($LIMPETS - $PREDEPLOYLIMPETS) " Limpet mines picked up from sector!*"
elseif (($PREDEPLOYLIMPETS = $LIMPETS) and (($LIMPETOWNER <> "belong to your Corp") and ($LIMPETOWNER <> "yours")))
	send "'{" $BOT_NAME "} - Enemy limpet(s) present in sector, cannot deploy!*"
end
goto :WAIT_FOR_COMMAND

:EXIT

:XENTER
gosub :KILLTHETRIGGERS
gosub :QUIKSTATS
isnumber $TEST $PARM1
if ($TEST = FALSE)
	setvar $PARM1 1
else
	if ($PARM1 <= 0)
		setvar $PARM1 1
	end
end
getwordpos $USER_COMMAND_LINE $POS "fill"
if ($POS > 0)
	setvar $REFILL TRUE
else
	setvar $REFILL FALSE
end
setvar $STARTINGLOCATION $CURRENT_PROMPT
setvar $VALIDPROMPTS "Command Citadel"
gosub :CHECKSTARTINGPROMPT
if ($STARTINGLOCATION = "Citadel")
	send "q "
	gosub :GETPLANETINFO
	send "q "
end

:EXIT_XENTER
setvar $I 1
while ($I <= $PARM1)
	send "q y n *"
	waiton "Enter your choice:"
	send "t* * *" $PASSWORD "*    *    *       za9999*   z*   "
	if (($CURRENT_SECTOR > 10) and ($CURRENT_SECTOR <> STARDOCK))
		if (($STARTINGLOCATION = "Citadel") or ($REFILL <> TRUE))
			send "f z1* z c d * "
		else
			setvar $TO_DROP "d"
			gosub :DO_TOPOFF
		end
	end
	if ($STARTINGLOCATION = "Citadel")
		send "l j"&#8&$PLANET&"*  m * * * c "
	end
	if ($I <> $PARM1)
		send "q q "
	end
	add $I 1
end

:DONEEXITENTER
gosub :KILLTHETRIGGERS
if ($PARM1 > 1)
	setvar $MESSAGE "Exit Enter - "&$PARM1&" times completed."
else
	setvar $MESSAGE "Exit Enter."
end
send "'{" $BOT_NAME "} - " $MESSAGE "*"
waiton "{"&$BOT_NAME&"} - "&$MESSAGE
goto :WAIT_FOR_COMMAND

:CLEAR
gosub :BIGDELAY_KILLTHETRIGGERS
gosub :QUIKSTATS
setvar $STARTINGLOCATION $CURRENT_PROMPT
setvar $VALIDPROMPTS "Command Citadel"
gosub :CHECKSTARTINGPROMPT
if ($STARTINGLOCATION = "Citadel")
	send "q "
	gosub :GETPLANETINFO
	send "q  "
end
send "'{" $BOT_NAME "} - Clearing Current Sector*"
setvar $BEFORELIMPETS $LIMPETS
setvar $BEFOREARMIDS $ARMIDS
setvar $PLACEDLIMPET FALSE
setvar $PLACEDARMID FALSE
send "** "
waiton "Warps to Sector(s) :"
setvar $LIMPETOWNER SECTOR.LIMPETS.OWNER[$CURRENT_SECTOR]
setvar $ARMIDOWNER SECTOR.MINES.OWNER[$CURRENT_SECTOR]
if (($LIMPETS <= 0) and (($LIMPETOWNER <> "belong to your Corp") and ($LIMPETOWNER <> "yours")))
	send "'{" $BOT_NAME "} - Need limpets to clear this sector*"
	goto :WAIT_FOR_COMMAND
end
if (($ARMIDS <= 0) and (($ARMIDOWNER <> "belong to your Corp") and ($ARMIDOWNER <> "yours")))
	send "'{" $BOT_NAME "} - Need armids to clear this sector*"
	goto :WAIT_FOR_COMMAND
end
gosub :CLEAR_SECTOR_DEPLOYEQUIPMENT
while (($PLACEDLIMPET = FALSE) or ($PLACEDARMID = FALSE))
	gosub :CLEAR_SECTOR_ATTEMPTCLEARINGMINES
end
if ($STARTINGLOCATION = "Citadel")
	gosub :LANDINGSUB
end
setsectorparameter $CURRENT_SECTOR "LIMPSEC" TRUE
setsectorparameter $CURRENT_SECTOR "MINESEC" TRUE
goto :WAIT_FOR_COMMAND

:CLEAR_SECTOR_ATTEMPTCLEARINGMINES
setvar $I 0
while ($I < 10)
	gosub :CLEAR_SECTOR_XENTER
	add $I 1
end
gosub :CLEAR_SECTOR_DEPLOYEQUIPMENT
return

:CLEAR_SECTOR_XENTER
send "q y n *"
waiton "Enter your choice:"
send "t* * *" $PASSWORD "*    *    *       za9999*   z*   "
return

:CLEAR_SECTOR_DEPLOYEQUIPMENT
if ($ARMIDS < 3)
	setvar $MINESTODEPLOY $ARMIDS
else
	setvar $MINESTODEPLOY 3
end
if ($LIMPETS < 3)
	setvar $LIMPSTODEPLOY $LIMPETS
else
	setvar $LIMPSTODEPLOY 3
end
setvar $CLEARMAC ""
if (($ARMIDOWNER <> "belong to your Corp") and ($ARMIDOWNER <> "yours"))
	setvar $CLEARMAC $CLEARMAC&"h  1  z "&$MINESTODEPLOY&"*  z c  *  "
end
if (($LIMPETOWNER <> "belong to your Corp") and ($LIMPETOWNER <> "yours"))
	setvar $CLEARMAC $CLEARMAC&"h  2  z "&$LIMPSTODEPLOY&"*  z c  *   "
end
send $CLEARMAC
gosub :QUIKSTATS
if (($BEFORELIMPETS > $LIMPETS) or ($LIMPETOWNER = "belong to your Corp") or ($LIMPETOWNER = "yours"))
	setvar $PLACEDLIMPET TRUE
end
if (($BEFOREARMIDS > $ARMIDS) or ($ARMIDOWNER = "belong to your Corp") or ($ARMIDOWNER = "yours"))
	setvar $PLACEDARMID TRUE
end
return

:SHUTDOWN
setvar $MODE "General"
goto :WAIT_FOR_COMMAND

:KEEPALIVE
add $ALIVE_COUNT 1
if ($ALIVE_COUNT >= ($ECHOINTERVAL * 2))
	setvar $ALIVE_COUNT 0
	gosub :CURRENT_PROMPT
	getsectorparameter 2 "FIG_COUNT" $FIGCOUNT
	echo ANSI_14 "*-= Time: " ANSI_15 TIME ANSI_14 " Fig Grid: " ANSI_15 $FIGCOUNT ANSI_14 " =-*" ANSI_7
	echo CURRENTANSILINE
end
if ((CONNECTED <> TRUE) and ($DORELOG = TRUE))
	goto :RELOG_ATTEMPT
end
send #27
setdelaytrigger KEEPALIVE :KEEPALIVE 30000
pause

:DEP

:D
gosub :BANKPROTECTIONS
if ($PARM1 <= 0)
	setvar $CASHTOTRANSFER $CREDITS
else
	setvar $CASHTOTRANSFER $PARM1
end
send "D"
waiton "Citadel treasury contains "
getword CURRENTLINE $CITADELCASH 4
striptext $CITADELCASH ","
if (($CASHTOTRANFER + $CITADELCASH) >= $CITADEL_CASH_MAX)
	send "'{"&$BOT_NAME&"} - Citadel has too much cash to do transfer (how sad for you)*"
	goto :WAIT_FOR_COMMAND
end
send "t t "&$CASHTOTRANSFER&"* "
send "'{"&$BOT_NAME&"} - "&$CASHTOTRANSFER&" credits deposited into citadel.*"
goto :WAIT_FOR_COMMAND

:WITH

:W
gosub :BANKPROTECTIONS
if ($PARM1 > $PLAYER_CASH_MAX)
	send "'{"&$BOT_NAME&"} - Can't withdraw more than 1 bil at a time*"
	goto :WAIT_FOR_COMMAND
end
if ($PARM1 <= 0)
	setvar $CASHTOTRANSFER $PLAYER_CASH_MAX
else
	setvar $CASHTOTRANSFER $PARM1
end
send "D"
waiton "Citadel treasury contains "
getword CURRENTLINE $CITADELCASH 4
striptext $CITADELCASH ","
if (($CREDITS + $CASHTOTRANSFER) > $PLAYER_CASH_MAX)
	setvar $CASHTOTRANSFER ($PLAYER_CASH_MAX - $CREDITS)
end
if ($CITADELCASH < $CASHTOTRANSFER)
	setvar $CASHTOTRANSFER $CITADELCASH
end
send "t f "&$CASHTOTRANSFER&"* "
send "'{"&$BOT_NAME&"} - "&$CASHTOTRANSFER&" credits taken from citadel.*"
goto :WAIT_FOR_COMMAND

:BANKPROTECTIONS
gosub :QUIKSTATS
setvar $VALIDPROMPTS "Citadel"
gosub :CHECKSTARTINGPROMPT
isnumber $TEST $PARM1
if ($TEST = FALSE)
	send "'{"&$BOT_NAME&"} - Cash entered is not a number, try again.*"
	goto :WAIT_FOR_COMMAND
end
return

:UNLOCK
setvar $UNLOCK_ATTEMPT 0
gosub :CURRENT_PROMPT
setvar $VALIDPROMPTS "Citadel"
gosub :CHECKSTARTINGPROMPT
send "'{" $BOT_NAME "} - Unlock ship initiated*ryy"
settextlinetrigger UNLOCK_MENU :UNLOCK_MENU "Game Server"
settexttrigger UNLOCK_OMENU :UNLOCK_MENU2 "Trade Wars 2002"

:UNLOCK_TRYAGAIN
setdelaytrigger UNLOCK_ANSIMENU :UNLOCK_ANSIMENU 2000
pause

:UNLOCK_ANSIMENU
if ($UNLOCK_ATTEMPT < 10)
	add $UNLOCK_ATTEMPT 1
	send "#"
	goto :UNLOCK_TRYAGAIN
end
disconnect
goto :WAIT_FOR_COMMAND

:UNLOCK_MENU2
gosub :KILLTHETRIGGERS
send "x * *"
waiton "Server"

:UNLOCK_MENU
gosub :KILLTHETRIGGERS
send $LETTER&"*"
waiton "module now loading."
send "**"
waiton "Enter your choice:"
send "t***"
waiton "Password?"
send $PASSWORD&"* * * c"
waiton "Citadel command (?=help)"
send "'{" $BOT_NAME "} - Ship has been unlocked!*"
goto :WAIT_FOR_COMMAND

:TOW
gosub :QUIKSTATS
setvar $VALIDPROMPTS "Command"
gosub :CHECKSTARTINGPROMPT
isnumber $TEST $PARM1
if ($TEST = FALSE)
	send "'{" $BOT_NAME "} - Ship to tow must be entered as a number*"
	goto :WAIT_FOR_COMMAND
elseif ($PARM1 < 1)
	send "'{" $BOT_NAME "} - Ship to tow must be entered as a number*"
	goto :WAIT_FOR_COMMAND
else
	setvar $SHIPTOTOW $PARM1
end

:TOWCHECK
killalltriggers
send "w"
settexttrigger TOWOFFCONTINUE :TOWCHECK "You shut off your Tractor Beam."
settexttrigger TOWOFF :TOWCONTINUE "Do you wish to tow a manned ship? (Y/N)"
pause

:TOWCONTINUE
killalltriggers
send "*"
settexttrigger TOWNOGO :TOWNOGO "You do not own any other ships in this sector!"
settexttrigger TOWREADY :TOWOFF "Choose which ship to tow (Q=Quit)"
pause

:TOWOFF
killalltriggers
send $SHIPTOTOW&"*"
settexttrigger TOWNOGO2 :TOWNOGO2 "Command [TL="
settexttrigger TOW_PASSWORD :TOW_PASSWORD "Enter the password for"
settextlinetrigger WAITONTOW :GOODTOW "You lock your Tractor Beam on "
pause

:TOW_PASSWORD
killalltriggers
send "*"
setvar $MESSAGE "That ship has a PassWord Set.*"
gosub :SWITCHBOARD
goto :WAIT_FOR_COMMAND

:TOWNOGO
killalltriggers
setvar $MESSAGE "There are no ships in the sector I can tow.*"
gosub :SWITCHBOARD
goto :WAIT_FOR_COMMAND

:TOWNOGO2
killalltriggers
setvar $MESSAGE "That ship number is not in the sector.*"
gosub :SWITCHBOARD
goto :WAIT_FOR_COMMAND

:GOODTOW
killalltriggers
setvar $MESSAGE "Tow locked onto ship number "&$SHIPTOTOW&"*"
gosub :SWITCHBOARD
goto :WAIT_FOR_COMMAND

:STATUS
gosub :QUIKSTATS
setvar $STARTINGLOCATION $CURRENT_PROMPT
if (($STARTINGLOCATION = "Command") or ($STARTINGLOCATION = "Citadel"))
	gosub :GETINFO
	if ($NOFLIP)
		send "CQ"
	else
		send "C N 9 Q Q "
	end
	waiton "Computer command [TL="
	gettext CURRENTLINE $TIMELEFT "Computer command [TL=" "]:"
else
	setvar $IGSTAT "Invalid Prompt"
	setvar $TIMELEFT "Invalid Prompt"
end
send "'*"
send "{" $BOT_NAME "}   --- Status Report ---*"
send "     - Sector      = " $CURRENT_SECTOR "*"
send "     - Prompt      = " $CURRENT_PROMPT "*"
if ($UNLIMITEDGAME)
	send "     - Turns       = Unlimited*"
else
	send "     - Turns       = " $TURNS "*"
end
send "     - Photons     = " $PHOTONS "*"
send "     - Mode        = " $MODE "*"
send "     - IG          = " $IGSTAT "*"
send "     - Ship        = " $SHIP_NUMBER "*"
if (($STARTINGLOCATION = "Planet") or ($STARTINGLOCATION = "Citadel"))
	if ($PLANET = 0)
		send "     - Planet      = None*"
	else
		send "     - Planet      = " $PLANET "*"
	end
else
	if ($PLANET = 0)
		send "     - Last Planet = None*"
	else
		send "     - Last Planet = " $PLANET "*"
	end
end
if ($BOT_TEAM_NAME = "misanthrope")
	send "     - Team        = None*"
else
	send "     - Team        = " $BOT_TEAM_NAME "*"
end
if ($TIMELEFT = "00:00:00")
	send "     - Time Left   = Unlimited*"
else
	send "     - Time Left   = "&$TIMELEFT&"*"
end
if ($NOFLIP = 0)
	send "     - CN9 Check   = Reset To SPACE*"
end
send "*"
goto :WAIT_FOR_COMMAND

:ONLINE_WATCH
gosub :KILLTHETRIGGERS
if ((CONNECTED <> TRUE) and ($DORELOG = TRUE))
	goto :RELOG_ATTEMPT
end
seteventtrigger RELOG2 :RELOG_ATTEMPT "CONNECTION LOST"
settextlinetrigger WHOS :WHOS "Who's Playing"
settexttrigger ALTERNATE :WHOS ""
setdelaytrigger VERIFYDELAY :VERIFYDELAY 3000
send "#"
pause

:WHOS
gosub :KILLTHETRIGGERS
goto :WAIT_FOR_COMMAND

:VERIFYDELAY
gosub :KILLTHETRIGGERS
disconnect

:RELOG_ATTEMPT
if ($DORELOG <> TRUE)
	goto :WAIT_FOR_COMMAND
end
killalltriggers
setdelaytrigger WAITFORRELOGDELAY :CONTINUEDOINGRELOG 1500
pause

:CONTINUEDOINGRELOG
gosub :DO_RELOG

:ENTER
gosub :RELOG_FREEZE_TRIGGER
killtrigger RELOG
killtrigger RELOG2
killtrigger FIRSTPAUSE
send "T*"
settexttrigger SHOWTODAY :CONTINUESHOWTODAY "Show today's log?"
pause

:CONTINUESHOWTODAY
gosub :RELOG_FREEZE_TRIGGER
send "*"
settexttrigger PAUSE2 :CONTINUEPAUSE2 "[Pause]"
pause

:CONTINUEPAUSE2
gosub :RELOG_FREEZE_TRIGGER
send "*"
settexttrigger PASSWORD :CONTINUEPASSWORD "A password is required to enter this game."
pause

:CONTINUEPASSWORD
gosub :RELOG_FREEZE_TRIGGER
send $PASSWORD&"*"

:ALLDONE_RELOG
killtrigger CLEARVOIDS
killtrigger NOVOIDS
killtrigger MOREPAUSES
gosub :RELOG_FREEZE_TRIGGER
send "Z*  *  Z*  Z   A 9999*  Z*  "
send "'{" $BOT_NAME "} - Auto-relog activated*"
settexttrigger AUTORELOGMESSAGE :CONTINUERELOGMESSAGE "{"&$BOT_NAME&"} - Auto-relog activated"
pause

:CONTINUERELOGMESSAGE
gosub :QUIKSTATS
gosub :RELOG_FREEZE_TRIGGER
if ($CURRENT_PROMPT = "Planet")
	send "*"
	gosub :GETPLANETINFO
	if ($CITADEL > 0)
		send "c "
		send "'{" $BOT_NAME "} - In citadel, planet "&$PLANET&".*"
		goto :WAIT_FOR_COMMAND
	else
		send "'{" $BOT_NAME "} - On planet "&$PLANET&".*"
		goto :WAIT_FOR_COMMAND
	end
end
loadvar $PLANET
if (($PLANET <> 0) and (($CURRENT_SECTOR <> 1) and ($CURRENT_SECTOR <> $STARDOCK)))
	setvar $LANDON $PLANET
	setvar $USER_COMMAND_LINE "land "&$LANDON
	goto :RUNUSERCOMMANDLINE
end
goto :WAIT_FOR_COMMAND

:DO_RELOG

:THEDELAY
if (CONNECTED <> TRUE)
	connect
end
killtrigger CONTINUELOGIN
killtrigger THEDELAY
killtrigger THEDELAY2
killtrigger THADELAY
killtrigger RELOG
killtrigger RELOG2
killtrigger RELOG89
killtrigger RELOG3
setdelaytrigger THEDELAY2 :THEDELAY 1500
seteventtrigger CONTINUELOGIN :CONTINUELOGIN "CONNECTION ACCEPTED"
seteventtrigger THEDELAY :THEDELAY "CONNECTION LOST"
seteventtrigger THADELAY :THEDELAY "Connection failure"
pause

:CONTINUELOGIN
killtrigger THADELAY
killtrigger THEDELAY2
killtrigger THEDELAY
killtrigger CONTINUELOGIN
if (CONNECTED <> TRUE)
	goto :DO_RELOG
end
killtrigger RELOG3
killtrigger RELOG
killtrigger RELOG2
settexttrigger RELOG3 :CONTINUERELOG3 "Please enter your name"
seteventtrigger RELOG2 :DO_RELOG "CONNECTION LOST"
pause

:CONTINUERELOG3
killtrigger RELOG89
killtrigger RELOG
killtrigger RELOG2
sound "page.wav"
send $USERNAME&"*"
killtrigger RELOG3
killtrigger RELOG69
send "#"
settexttrigger RELOG69 :CONTINUERELOG4 "Make a Selection:"
settexttrigger RELOG3 :CONTINUERELOG4 "Selection (? for menu):"
seteventtrigger RELOG2 :DO_RELOG "CONNECTION LOST"
pause

:CONTINUERELOG4
send $LETTER

:EXTRAPAUSE
killtrigger RELOG
killtrigger RELOG2
killtrigger FIRSTPAUSE
killtrigger ENTER
killtrigger RELOG89
seteventtrigger RELOG2 :DO_RELOG "CONNECTION LOST"
settexttrigger FIRSTPAUSE :FIRSTPAUSE "[Pause]"
settexttrigger ENTER :DONE_DO_RELOG "Enter your choice"
pause

:FIRSTPAUSE
send "*"
settexttrigger FIRSTPAUSE :FIRSTPAUSE "[Pause]"
pause

:DONE_DO_RELOG
gosub :KILLTHETRIGGERS
return

:FIND

:FINDER

:F

:NF

:FP

:PORT

:DE

:UF

:FDE

:NFUP

:FUP

:NEAR
if (($COMMAND = "finder") or ($COMMAND = "find"))
	setvar $NEAR $PARM1
	setvar $SOURCE $PARM2
else
	setvar $NEAR $COMMAND
	setvar $SOURCE $PARM1
end
isnumber $NUMBER $SOURCE
if ($NUMBER = TRUE)
	if ($SOURCE <= 0)
		setvar $SOURCE CURRENTSECTOR
	end
	if ($SOURCE > SECTORS)
		send "'{" $BOT_NAME "} - That sector is out of bounds (Must be between 1-"&SECTORS&")*"
		goto :WAIT_FOR_COMMAND
	end
else
	if (($COMMAND = "finder") or ($COMMAND = "find"))
		setvar $PARM2 $PARM3
	else
		setvar $PARM2 $PARM1
	end
	setvar $SOURCE CURRENTSECTOR
end
getsectorparameter $SOURCE "FIGSEC" $ISFIGGED
if ($ISFIGGED = "")
	send "'{" $BOT_NAME "} - It appears no grid data is available.  Run a fighter grid checker that uses the sector parameter FIGSEC. (Try figs command)*"
	goto :WAIT_FOR_COMMAND
end
if (($NEAR <> "owner") and (($NEAR <> "ufde") and (($NEAR <> "f") and (($NEAR <> "nf") and (($NEAR <> "fde") and (($NEAR <> "uf") and (($NEAR <> "fp") and (($NEAR <> "nfup") and (($NEAR <> "fup") and (($NEAR <> "p") and (($NEAR <> "de") and (($NEAR <> "fig") and (($NEAR <> "nofig") and (($NEAR <> "figport") and (($NEAR <> "port") and ($NEAR <> "deadend"))))))))))))))))
	send "'{" $BOT_NAME "} - Please use - [type] [sector] format*"
	goto :WAIT_FOR_COMMAND
end
if (($NEAR = "fp") or ($NEAR = "port") or ($NEAR = "p") or ($NEAR = "nfup") or ($NEAR = "fup"))
	getlength $PARM2 $PLENGTH
	if (($PARM2 = 0) or ($PLENGTH <> 3))
		setvar $PARM2 "xxx"
	end
	setvar $INVALID FALSE
	cuttext $PARM2 $PFUEL 1 1
	if (($PFUEL <> "s") and (($PFUEL <> "b") and ($PFUEL <> "x")))
		setvar $INVALID TRUE
	end
	cuttext $PARM2 $PORG 2 1
	if (($PORG <> "s") and (($PORG <> "b") and ($PORG <> "x")))
		setvar $INVALID TRUE
	end
	cuttext $PARM2 $PEQUIP 3 1
	if (($PEQUIP <> "s") and (($PEQUIP <> "b") and ($PEQUIP <> "x")))
		setvar $INVALID TRUE
	end
	if ($INVALID)
		send "'*{" $BOT_NAME "} - Invalid Port Type*"
		send "  - Please use - [fp/p] [sector] [port type] format **"
		goto :WAIT_FOR_COMMAND
	end
	setvar $PTYPE $PARM2
	uppercase $PTYPE
end

:NEAR_HIT
getsectorparameter $SOURCE "FIGSEC" $ISFIGGED
getword SECTOR.FIGS.OWNER[$SOURCE] $FIGOWNER 3
setvar $SOURCE_MESSAGE ""
if (($NEAR = "f") and ($ISFIGGED = TRUE))
	setvar $SOURCE_MESSAGE "appears to be fig'd."
elseif (($NEAR = "owner") and (($ISFIGGED <> TRUE) and ($FIGOWNER = "Corp#"&$TARGET_CORP&",")))
	setvar $SOURCE_MESSAGE "appears to be fig'd by corp #"&$TARGET_CORP&"."
elseif ((($NEAR = "nf") or ($NEAR = "uf")) and ($ISFIGGED <> TRUE))
	setvar $SOURCE_MESSAGE "is not figged."
elseif (($NEAR = "ufde") and (($ISFIGGED = FALSE) and (SECTOR.WARPCOUNT[$SOURCE] = 1)))
	setvar $SOURCE_MESSAGE "appears to be an unfigged dead-end."
elseif (($NEAR = "fde") and (($ISFIGGED = TRUE) and (SECTOR.WARPCOUNT[$SOURCE] = 1)))
	setvar $SOURCE_MESSAGE "appears to be a figged dead-end."
elseif (($NEAR = "fp") and (($ISFIGGED = TRUE) and ((PORT.CLASS[$SOURCE] > 0) and (PORT.CLASS[$SOURCE] < 9))))
	if (($PFUEL = "b") and (PORT.BUYFUEL[$SOURCE] = 1)) or (($PFUEL = "s") and (PORT.BUYFUEL[$SOURCE] = 0)) or ($PFUEL = "x")
		if (($PORG = "b") and (PORT.BUYORG[$SOURCE] = 1)) or (($PORG = "s") and (PORT.BUYORG[$SOURCE] = 0)) or ($PORG = "x")
			if (($PEQUIP = "b") and (PORT.BUYEQUIP[$SOURCE] = 1)) or (($PEQUIP = "s") and (PORT.BUYEQUIP[$SOURCE] = 0)) or ($PEQUIP = "x")
				setvar $SOURCE_MESSAGE " has a "&$PTYPE&" port that's figged."
			end
		end
	end
elseif ((($NEAR = "port") or ($NEAR = "p")) and ((PORT.CLASS[$SOURCE] > 0) and (PORT.CLASS[$SOURCE] < 9)))
	if (($PFUEL = "b") and (PORT.BUYFUEL[$SOURCE] = 1)) or (($PFUEL = "s") and (PORT.BUYFUEL[$SOURCE] = 0)) or ($PFUEL = "x")
		if (($PORG = "b") and (PORT.BUYORG[$SOURCE] = 1)) or (($PORG = "s") and (PORT.BUYORG[$SOURCE] = 0)) or ($PORG = "x")
			if (($PEQUIP = "b") and (PORT.BUYEQUIP[$SOURCE] = 1)) or (($PEQUIP = "s") and (PORT.BUYEQUIP[$SOURCE] = 0)) or ($PEQUIP = "x")
				setvar $SOURCE_MESSAGE " has a "&$PTYPE&" port."
			end
		end
	end
elseif (((($NEAR = "fup") and ($ISFIGGED = TRUE)) or (($NEAR = "nfup") and ($ISFIGGED <> TRUE))) and ((PORT.CLASS[$SOURCE] > 0) and (PORT.CLASS[$SOURCE] < 9)))
	setvar $FOUNDFUELPORT FALSE
	setvar $FOUNDORGPORT FALSE
	setvar $FOUNDEQUIPPORT FALSE
	if ((($PFUEL = "b") and (PORT.BUYFUEL[$SOURCE] = 1)) and (PORT.FUEL[$SOURCE] >= 10000)) or ((($PFUEL = "s") and (PORT.BUYFUEL[$SOURCE] = 0)) and (PORT.FUEL[$SOURCE] >= 10000))
		setvar $FOUNDFUELPORT TRUE
	end
	if ((($PORG = "b") and (PORT.BUYORG[$SOURCE] = 1)) and (PORT.ORG[$SOURCE] >= 10000)) or ((($PORG = "s") and (PORT.BUYORG[$SOURCE] = 0)) and (PORT.ORG[$SOURCE] >= 10000))
		setvar $FOUNDORGPORT TRUE
	end
	if ((($PEQUIP = "b") and (PORT.BUYEQUIP[$SOURCE] = 1)) and (PORT.EQUIP[$SOURCE] >= 10000)) or ((($PEQUIP = "s") and (PORT.BUYEQUIP[$SOURCE] = 0)) and (PORT.EQUIP[$SOURCE] >= 10000))
		setvar $FOUNDEQUIPPORT TRUE
	end
	if (($PFUEL = "x") and (($PORG = "x") and ($PEQUIP = "x")))
		if (($PFUEL = "x") and (PORT.FUEL[$SOURCE] >= 10000)) or (($PORG = "x") and (PORT.ORG[$SOURCE] >= 10000)) or (($PEQUIP = "x") and (PORT.EQUIP[$SOURCE] >= 10000))
			setvar $FOUNDFUELPORT TRUE
			setvar $FOUNDORGPORT TRUE
			setvar $FOUNDEQUIPPORT TRUE
		end
	else
		if ($PFUEL = "x")
			setvar $FOUNDFUELPORT TRUE
		end
		if ($PORG = "x")
			setvar $FOUNDORGPORT TRUE
		end
		if ($PEQUIP = "x")
			setvar $FOUNDEQUIPPORT TRUE
		end
	end
	if (($FOUNDFUELPORT = TRUE) and (($FOUNDORGPORT = TRUE) and ($FOUNDEQUIPPORT = TRUE)))
		if ($NEAR = "fup")
			setvar $SOURCE_MESSAGE " has an upped "&$PTYPE&" port that's figged."
		else
			setvar $SOURCE_MESSAGE " has an upped "&$PTYPE&" port that's not figged."
		end
	end
end
gosub :BREADTH_SEARCH
if ($RETURN_DATA <> "")
	send "'*{" $BOT_NAME "}*  - "&$RETURN_DATA
	if ($SOURCE_MESSAGE <> "")
		getsectorparameter $SOURCE "MINESEC" $ISMINED3
		getsectorparameter $SOURCE "LIMPSEC" $ISLIMPD3
		if (($ISLIMPD3 <> 0) and ($ISMINED3 <> 0))
			send "*   *   Note: "&$SOURCE&"LA, "&$SOURCE_MESSAGE
		else
			if ($ISLIMPD3 <> 0)
				send "*   *   Note: "&$SOURCE&"L, "&$SOURCE_MESSAGE
			elseif ($ISMINED3 <> 0)
				send "*   *   Note: "&$SOURCE&"A, "&$SOURCE_MESSAGE
			else
				send "*   *   Note: "&$SOURCE&", "&$SOURCE_MESSAGE
			end
		end
	end
	send "**"
end
goto :WAIT_FOR_COMMAND

:BREADTH_SEARCH
setvar $I 1
setvar $LOOP_DATA 1
getnearestwarps $NEARARRAY $SOURCE
while ($I <= $NEARARRAY)
	setvar $FOCUS $NEARARRAY[$I]
	getdistance $_DIST_1_ $FOCUS $SOURCE
	getdistance $_DIST_2_ $SOURCE $FOCUS
	if ($_DIST_1_ = $_DIST_2_)
		getsectorparameter $FOCUS "FIGSEC" $ISFIGGED2
		getword SECTOR.FIGS.OWNER[$FOCUS] $FIGOWNER 3
		if ((($SOURCE <> $FOCUS) and (($FOCUS > 10) and ($FOCUS <> $STARDOCK))) and ((($ISFIGGED2 = FALSE) and (($NEAR = "uf") or ($NEAR = "nf") or (($NEAR = "owner") and ($FIGOWNER = "Corp#"&$TARGET_CORP&",")) or (($NEAR = "de") and (SECTOR.WARPCOUNT[$FOCUS] = 1)))) or (($ISFIGGED2 = TRUE) and (($NEAR = "f") or (($NEAR = "fde") and (SECTOR.WARPCOUNT[$FOCUS] = 1))))))
			getcourse $COURSE $SOURCE $FOCUS
			setvar $I 1
			setvar $FCOUNT 0
			setvar $DIRECTIONS ""
			if ($NEAR = "f")
				setvar $MESSAGE "Nearest Fig"
			elseif (($NEAR = "uf") or ($NEAR = "nf"))
				setvar $MESSAGE "Nearest Non-Fig"
			elseif ($NEAR = "owner")
				setvar $MESSAGE "Nearest Corp #"&$TARGET_CORP&" Fig"
			elseif ($NEAR = "de")
				setvar $MESSAGE "Nearest Non-Fig DE"
			elseif ($NEAR = "fde")
				setvar $MESSAGE "Nearest Fig'd DE"
			end
			if ($COURSE = 1)
				while (SECTOR.WARPS[$SOURCE][$I] > 0)
					setvar $TEMPCHECK SECTOR.WARPS[$SOURCE][$I]
					getsectorparameter $TEMPCHECK "FIGSEC" $ISFIGGED3
					getsectorparameter $TEMPCHECK "MINESEC" $ISMINED3
					getsectorparameter $TEMPCHECK "LIMPSEC" $ISLIMPD3
					getword SECTOR.FIGS.OWNER[$TEMPCHECK] $FIGOWNER2 3
					if (($ISFIGGED3 = TRUE) and (($NEAR = "f") or (($NEAR = "fde") and (SECTOR.WARPCOUNT[$TEMPCHECK] = 1)))) or (($ISFIGGED3 = FALSE) and ((($NEAR = "owner") and ($FIGOWNER2 = "Corp#"&$TARGET_CORP&",")) or ($NEAR = "uf") or ($NEAR = "nf") or (($NEAR = "de") and (SECTOR.WARPCOUNT[$TEMPCHECK] = 1))))
						setvar $DIRECTIONS $DIRECTIONS&$TEMPCHECK
						if (($ISMINED3 <> 0) and ($ISLIMPD3 <> 0))
							setvar $DIRECTIONS $DIRECTIONS&"LA"
						else
							if ($ISMINED3 <> 0)
								setvar $DIRECTIONS $DIRECTIONS&"A"
							elseif ($ISLIMPD3 <> 0)
								setvar $DIRECTIONS $DIRECTIONS&"L"
							end
						end
						setvar $DIRECTIONS $DIRECTIONS&" "
						add $FCOUNT 1
					end
					add $I 1
				end
				if ($FCOUNT > 1)
					setvar $RETURN_DATA $MESSAGE&"s adjacent to "&$SOURCE&" are*    [ "&$DIRECTIONS&"]"
				else
					setvar $RETURN_DATA $MESSAGE&" adjacent to "&$SOURCE&" is*    [ "&$DIRECTIONS&"]"
				end
			else
				while ($I <= ($COURSE + 1))
					getsectorparameter $COURSE[$I] "MINESEC" $ISMINED3
					getsectorparameter $COURSE[$I] "LIMPSEC" $ISLIMPD3
					if (($ISMINED3 <> 0) and ($ISLIMPD3 <> 0))
						setvar $DIRECTIONS "LA "&$DIRECTIONS
					else
						if ($ISMINED3 <> 0)
							setvar $DIRECTIONS "A "&$DIRECTIONS
						end
						if ($ISLIMPD3 <> 0)
							setvar $DIRECTIONS "L "&$DIRECTIONS
						end
					end
					setvar $DIRECTIONS " "&$COURSE[$I]&$DIRECTIONS
					add $I 1
				end
				setvar $RETURN_DATA $MESSAGE&" to "&$SOURCE&" is "&$FOCUS&" ("&$COURSE&" hops)*  <<"&$DIRECTIONS&" >> "
			end
			return
		elseif (($NEAR = "nfup") and ($ISFIGGED2 = FALSE)) or (($NEAR = "fup") and ($ISFIGGED2 = TRUE))
			setvar $FOUNDFUELPORT FALSE
			setvar $FOUNDORGPORT FALSE
			setvar $FOUNDEQUIPPORT FALSE
			if (((PORT.CLASS[$FOCUS] > 0) and (PORT.CLASS[$FOCUS] < 9)) and ($FOCUS <> $SOURCE))
				if ((($PFUEL = "b") and (PORT.BUYFUEL[$FOCUS] = 1)) and (PORT.FUEL[$FOCUS] >= 10000)) or ((($PFUEL = "s") and (PORT.BUYFUEL[$FOCUS] = 0)) and (PORT.FUEL[$FOCUS] >= 10000))
					setvar $FOUNDFUELPORT TRUE
				end
				if ((($PORG = "b") and (PORT.BUYORG[$FOCUS] = 1)) and (PORT.ORG[$FOCUS] >= 10000)) or ((($PORG = "s") and (PORT.BUYORG[$FOCUS] = 0)) and (PORT.ORG[$FOCUS] >= 10000))
					setvar $FOUNDORGPORT TRUE
				end
				if ((($PEQUIP = "b") and (PORT.BUYEQUIP[$FOCUS] = 1)) and (PORT.EQUIP[$FOCUS] >= 10000)) or ((($PEQUIP = "s") and (PORT.BUYEQUIP[$FOCUS] = 0)) and (PORT.EQUIP[$FOCUS] >= 10000))
					setvar $FOUNDEQUIPPORT TRUE
				end
				if (($PFUEL = "x") and (($PORG = "x") and ($PEQUIP = "x")))
					if (($PFUEL = "x") and (PORT.FUEL[$FOCUS] >= 10000)) or (($PORG = "x") and (PORT.ORG[$FOCUS] >= 10000)) or (($PEQUIP = "x") and (PORT.EQUIP[$FOCUS] >= 10000))
						setvar $FOUNDFUELPORT TRUE
						setvar $FOUNDORGPORT TRUE
						setvar $FOUNDEQUIPPORT TRUE
					end
				else
					if ($PFUEL = "x")
						setvar $FOUNDFUELPORT TRUE
					end
					if ($PORG = "x")
						setvar $FOUNDORGPORT TRUE
					end
					if ($PEQUIP = "x")
						setvar $FOUNDEQUIPPORT TRUE
					end
				end
				if (($FOUNDFUELPORT = TRUE) and (($FOUNDORGPORT = TRUE) and ($FOUNDEQUIPPORT = TRUE)))
					if ($LOOP_DATA = 1)
						getcourse $COURSE $SOURCE $FOCUS
						setvar $RETURN_DATA "Nearest Figged upgraded "&$PTYPE&" port(s) to "&$SOURCE&": "&$FOCUS&" ("&$COURSE&" hops)"
					elseif ($LOOP_DATA = 2)
						getcourse $COURSE $SOURCE $FOCUS
						setvar $RETURN_DATA $RETURN_DATA&", "&$FOCUS&" ("&$COURSE&" hops)"
					else
						getcourse $COURSE $SOURCE $FOCUS
						setvar $RETURN_DATA $RETURN_DATA&", and "&$FOCUS&" ("&$COURSE&" hops)"
						setvar $LOOP_DATA 1
						return
					end
					add $LOOP_DATA 1
				end
			end
		elseif ($NEAR = "port") or ($NEAR = "p") or (($NEAR = "fp") and ($ISFIGGED2 = TRUE))
			if (((PORT.CLASS[$FOCUS] > 0) and (PORT.CLASS[$FOCUS] < 9)) and ($FOCUS <> $SOURCE))
				if (($PFUEL = "b") and (PORT.BUYFUEL[$FOCUS] = 1)) or (($PFUEL = "s") and (PORT.BUYFUEL[$FOCUS] = 0)) or ($PFUEL = "x")
					if (($PORG = "b") and (PORT.BUYORG[$FOCUS] = 1)) or (($PORG = "s") and (PORT.BUYORG[$FOCUS] = 0)) or ($PORG = "x")
						if (($PEQUIP = "b") and (PORT.BUYEQUIP[$FOCUS] = 1)) or (($PEQUIP = "s") and (PORT.BUYEQUIP[$FOCUS] = 0)) or ($PEQUIP = "x")
							if ($LOOP_DATA = 1)
								getcourse $COURSE $SOURCE $FOCUS
								setvar $RETURN_DATA "Nearest Figged "&$PTYPE&" port(s) to "&$SOURCE&": "&$FOCUS&" ("&$COURSE&" hops)"
							elseif ($LOOP_DATA = 2)
								getcourse $COURSE $SOURCE $FOCUS
								setvar $RETURN_DATA $RETURN_DATA&", "&$FOCUS&" ("&$COURSE&" hops)"
							else
								getcourse $COURSE $SOURCE $FOCUS
								setvar $RETURN_DATA $RETURN_DATA&", and "&$FOCUS&" ("&$COURSE&" hops)"
								setvar $LOOP_DATA 1
								return
							end
							add $LOOP_DATA 1
						end
					end
				end
			end
		end
	end
	add $I 1
end
setvar $RETURN_DATA "Nothing found for that search."
return

:GETVAR
gosub :KILLTHETRIGGERS
getword $USER_COMMAND_LINE $PARM1 1
setvar $MESSAGE ""
if (($PARM1 = "h") or ($PARM1 = "home") or ($PARM1 = "all"))
	setvar $MESSAGE $MESSAGE&"Home Sector: "&$HOME_SECTOR&"*"
end
if (($PARM1 = "s") or ($PARM1 = "stardock") or ($PARM1 = "all"))
	setvar $MESSAGE $MESSAGE&"Stardock: "&$STARDOCK&"*"
end
if (($PARM1 = "r") or ($PARM1 = "rylos") or ($PARM1 = "all"))
	setvar $MESSAGE $MESSAGE&"Rylos: "&$RYLOS&"*"
end
if (($PARM1 = "a") or ($PARM1 = "alpha") or ($PARM1 = "all"))
	setvar $MESSAGE $MESSAGE&"Alpha Centauri: "&$ALPHA_CENTAURI&"*"
end
if (($PARM1 = "b") or ($PARM1 = "backdoor") or ($PARM1 = "all"))
	setvar $MESSAGE $MESSAGE&"Backdoor: "&$BACKDOOR&"*"
end
if (($PARM1 = "x") or ($PARM1 = "safeship") or ($PARM1 = "all"))
	setvar $MESSAGE $MESSAGE&"Safe Ship: "&$SAFE_SHIP&"*"
end
if (($PARM1 = "tl") or ($PARM1 = "turnlimit") or ($PARM1 = "all"))
	setvar $MESSAGE $MESSAGE&"Turn Limit: "&$BOT_TURN_LIMIT&"*"
end
if ($MESSAGE = "")
	setvar $MESSAGE "Unknown variable name entered.*"
end
if ($SELF_COMMAND <> TRUE)
	setvar $SELF_COMMAND 2
end
gosub :SWITCHBOARD
goto :WAIT_FOR_COMMAND

:SETVAR
gosub :KILLTHETRIGGERS
getword $USER_COMMAND_LINE $PARM1 1
isnumber $TEST $PARM2
if (($PARM1 = "h") or ($PARM1 = "home"))
	if ($TEST)
		if (($PARM2 <= SECTORS) and ($PARM2 >= 1))
			setvar $HOME_SECTOR $PARM2
			setvar $MESSAGE "Home Sector variable set to: "&$HOME_SECTOR&".*"
		else
			setvar $MESSAGE "Variable entered not valid, keeping old value.*"
		end
	end
elseif (($PARM1 = "s") or ($PARM1 = "stardock"))
	if ($TEST)
		if (($PARM2 <= SECTORS) and ($PARM2 >= 1))
			setvar $STARDOCK $PARM2
			setvar $MESSAGE "Stardock variable set to: "&$STARDOCK&".*"
		else
			setvar $MESSAGE "Variable entered not valid, keeping old value.*"
		end
	end
elseif (($PARM1 = "r") or ($PARM1 = "rylos"))
	if ($TEST)
		if (($PARM2 <= SECTORS) and ($PARM2 >= 1))
			setvar $RYLOS $PARM2
			setvar $MESSAGE "Rylos variable set to: "&$RYLOS&".*"
		else
			setvar $MESSAGE "Variable entered not valid, keeping old value.*"
		end
	end
elseif (($PARM1 = "a") or ($PARM1 = "alpha"))
	if ($TEST)
		if (($PARM2 <= SECTORS) and ($PARM2 >= 1))
			setvar $ALPHA_CENTAURI $PARM2
			setvar $MESSAGE "Alpha Centauri variable set to: "&$ALPHA_CENTAURI&".*"
		else
			setvar $MESSAGE "Variable entered not valid, keeping old value.*"
		end
	end
elseif (($PARM1 = "b") or ($PARM1 = "backdoor"))
	if ($TEST)
		if (($PARM2 <= SECTORS) and ($PARM2 >= 1))
			setvar $BACKDOOR $PARM2
			setvar $MESSAGE "Backdoor Sector variable set to: "&$BACKDOOR&".*"
		else
			setvar $MESSAGE "Variable entered not valid, keeping old value.*"
		end
	end
elseif (($PARM1 = "x") or ($PARM1 = "safeship"))
	if ($TEST)
		if ($PARM2 >= 1)
			setvar $SAFE_SHIP $PARM2
			setvar $MESSAGE "Safe Ship variable set to: "&$SAFE_SHIP&".*"
		else
			setvar $MESSAGE "Variable entered not valid, keeping old value.*"
		end
	end
elseif (($PARM1 = "tl") or ($PARM1 = "turnlimit"))
	if ($TEST)
		if ($PARM2 >= 0)
			setvar $BOT_TURN_LIMIT $PARM2
			setvar $MESSAGE "Turn Limit variable set to: "&$BOT_TURN_LIMIT&".*"
		else
			setvar $MESSAGE "Variable entered not valid, keeping old value.*"
		end
	end
elseif (($PARM1 = "pt") or ($PARM1 = "planet_trade"))
	if ($TEST)
		if ($PARM2 >= 0)
			setvar $BOT_PTRADESETTING $PARM2
			setvar $PTRADESETTING $PARM2
			setvar $MESSAGE "Planet Trade % variable set to: "&$BOT_PTRADESETTING&".*"
		else
			setvar $MESSAGE "Variable entered not valid, keeping old value.*"
		end
	end
elseif (($PARM1 = "rf") or ($PARM1 = "rob_factor"))
	if ($TEST)
		if ($PARM2 >= 0)
			setvar $BOT_ROB_FACTOR $PARM2
			setvar $ROB_FACTOR $PARM2
			setvar $MESSAGE "Rob Factor variable set to: "&$BOT_ROB_FACTOR&".*"
		else
			setvar $MESSAGE "Variable entered not valid, keeping old value.*"
		end
	end
elseif (($PARM1 = "sf") or ($PARM1 = "steal_factor"))
	if ($TEST)
		if ($PARM2 >= 0)
			setvar $STEAL_FACTOR $PARM2
			setvar $MESSAGE "Steal Factor variable set to: "&$STEAL_FACTOR&".*"
		else
			setvar $MESSAGE "Variable entered not valid, keeping old value.*"
		end
	end
else
	setvar $MESSAGE "Unknown variable name entered.*"
end
gosub :PREFERENCESTATS
gosub :SWITCHBOARD
goto :WAIT_FOR_COMMAND

:BUSTCOUNT

:COUNTBUST

:COUNTBUSTS
echo "**"
setvar $MESSAGE "Please StandBy, Counting*"
gosub :SWITCHBOARD
setvar $I 1
setvar $BUSTCOUNT 0
while ($I <= SECTORS)
	getsectorparameter $I "BUSTED" $ISBUSTED
	if ($ISBUSTED)
		add $BUSTCOUNT 1
	end
	add $I 1
end
setvar $MESSAGE "This bot currently has "&$BUSTCOUNT&" busts recorded in the universe*"
gosub :SWITCHBOARD
goto :WAIT_FOR_COMMAND

:PLIST
gosub :KILLTHETRIGGERS
setvar $PLANET 0
gosub :QUIKSTATS
setvar $PLANETOUTPUT ""
setvar $VALIDPROMPTS "Citadel Command"
gosub :CHECKSTARTINGPROMPT

:PLANET_LISTING_START
if ($STARTINGLOCATION = "Citadel")
	send "S* Q"
	gosub :GETPLANETINFO
	send "Q"
else
	send "** "
end
if ((SECTOR.PLANETCOUNT[$CURRENT_SECTOR] <= 1) and ($PLANET_SCANNER = "No"))
	send "'{"&$BOT_NAME&"} - Must be more than one planet in sector if bot doesn't have planet scanner*"
	if ($STARTINGLOCATION = "Citadel")
		gosub :LANDINGSUB
	end
	goto :WAIT_FOR_COMMAND
end
send "L"
settexttrigger BEGINSCAN :PLANET_LISTING_BEGINSCAN "Atmospheric maneuvering system engaged"
pause

:PLANET_LISTING_BEGINSCAN
gosub :KILLTHETRIGGERS
settextlinetrigger NOTHING2DO :PLANET_LISTING_NOTHING2DO "You can create one with a Genesis Torpedo"
settexttrigger PSCANDONE :PLANET_LISTING_PSCANDONE "Land on which planet"
settextlinetrigger LINE_TRIG :PLANET_LISTING_PARSE_SCAN_LINE
pause

:PLANET_LISTING_NOTHING2DO
gosub :KILLTHETRIGGERS
waiton "(?="
send "'{"&$BOT_NAME&"} - No Planets In Sector!*"
goto :WAIT_FOR_COMMAND

:PLANET_LISTING_PARSE_SCAN_LINE
killtrigger LINE_TRIG
setvar $S CURRENTLINE
if (($S = "") or ($S = 0))
	setvar $S "          "
end
replacetext $S "        Level" "Lvl"
replacetext $S "-----------------------------------------------" "-------------------------------------------"
replacetext $S "        Citadel" "Citadel"
replacetext $S "l Fighters Q" "l  Figs Q"
getlength $S $LENGTH
if ($LENGTH > 70)
	cuttext $S $S 1 70
end
setvar $PLANETOUTPUT $PLANETOUTPUT&$S&"*"
gosub :KILLTHETRIGGERS
goto :PLANET_LISTING_BEGINSCAN

:PLANET_LISTING_PSCANDONE
setvar $STRLOCAL ""
gosub :KILLTHETRIGGERS
setvar $IDX 1
if (($PLANET <> 0) and ($CURRENT_SECTOR <> 1))
	send $PLANET&"* c "
	setvar $MESSAGE "On Planet #"&$PLANET&"*"
else
	send " * "
	setvar $MESSAGE ""
end
waiton "(?="
send "'*"
waiton "Comm-link open on sub-space band"
send $PLANETOUTPUT
send "**"
waiton "Sub-space comm-link terminated"
gosub :SWITCHBOARD
goto :WAIT_FOR_COMMAND

:PEX

:PEL

:PELK

:PED

:PE
gosub :CHECK_INVADE_MACRO_PARAMS
setvar $SPEED_INVADE_MACRO $ENTER&"     *  "
setvar $NORMAL_INVADE_MACRO $ENTER&"*            "
goto :START_INVADE_MACRO

:PXEX

:PXEL

:PXELK

:PXE

:PXED
gosub :CHECK_INVADE_MACRO_PARAMS
setvar $SPEED_INVADE_MACRO $XPORT&$ENTER&"       * "
setvar $NORMAL_INVADE_MACRO $XPORT&$ENTER&"** "
goto :START_INVADE_MACRO

:START_INVADE_MACRO
if ($STARTINGLOCATION = "Citadel")
	setvar $MAC_STARTING $PHOTON&"q  q  "
else
	setvar $MAC_STARTING $PHOTON&"  "
end
if ($COMMAND = "pxex")
	setvar $MAC_ENDING "x   "&$PARM3&"*  q  q  z  n"
	setvar $ENDS_IN_SECTOR TRUE
elseif ($COMMAND = "pex")
	setvar $MAC_ENDING "x    "&$PARM2&"*  q  q  *  z  n  *  "
	setvar $ENDS_IN_SECTOR TRUE
elseif ($COMMAND = "pel")
	setvar $MAC_ENDING "l "&$PARM2&"*  *"
	setvar $ENDS_IN_SECTOR FALSE
elseif ($COMMAND = "pxel")
	setvar $MAC_ENDING "l "&$PARM3&"*  *  "
	setvar $ENDS_IN_SECTOR FALSE
elseif ($COMMAND = "pxelk")
	setvar $MAC_ENDING "l "&$PARM3&"*  *  a"&$SHIP_MAX_ATTACK&"*"
	setvar $ENDS_IN_SECTOR FALSE
elseif ($COMMAND = "pelk")
	setvar $MAC_ENDING "l "&$PARM2&"*  *  a"&$SHIP_MAX_ATTACK&"*"
	setvar $ENDS_IN_SECTOR FALSE
elseif (($COMMAND = "pxed") or ($COMMAND = "ped"))
	setvar $MAC_ENDING "u  y  n  . *  j  c  *  "
	setvar $ENDS_IN_SECTOR FALSE
else
	setvar $MAC_ENDING ""
	setvar $ENDS_IN_SECTOR FALSE
end
if (($STARTINGLOCATION = "Citadel") and ($ENDS_IN_SECTOR = TRUE))
	setvar $MAC_ENDING $MAC_ENDING&"l "&$PLANET&" * c"
end
setvar $MAC_ENDING $MAC_ENDING&"@"
send "  t"
waitfor ", 2"
getword CURRENTLINE $INITTIME 1

:PHOTON_ATTACK_TIMER
send "  t"
waitfor ", 2"
getword CURRENTLINE $CURRENTTIME 1
waitfor "Computer"
if ($INITTIME <> $CURRENTTIME)
	if ($SPEED = TRUE)
		send $MAC_STARTING&$SPEED_INVADE_MACRO&$MAC_ENDING
	else
		send $MAC_STARTING&$NORMAL_INVADE_MACRO&$MAC_ENDING
	end
else
	goto :PHOTON_ATTACK_TIMER
end
if ($SPEED = FALSE)
	setvar $I 1
	settextlinetrigger DAMAGE :COLLECT_DAMAGE "The console reports damages of "
	settextlinetrigger DAMAGE_DONE :DAMAGE_DONE "Average Interval Lag:"
	settextlinetrigger DAMAGE_POD :COLLECT_POD "You rush to an escape pod and abandon ship..."
	pause

	:COLLECT_DAMAGE
	setvar $SCAN_ARRAY[$I] CURRENTLINE
	add $I 1
	settextlinetrigger DAMAGE :COLLECT_DAMAGE "The console reports damages of "
	pause

	:COLLECT_POD
	setvar $SCAN_ARRAY[$I] CURRENTLINE
	add $I 1

	:DAMAGE_DONE
	gosub :KILLTHETRIGGERS
	if ($I > 1)
		setvar $J 1
		send "'*"
		settextlinetrigger COMM :CONTINUEDAMAGE "Comm-link open on sub-space band"
		pause

		:CONTINUEDAMAGE
		while ($J < $I)
			send $SCAN_ARRAY[$J]&"*"
			add $J 1
		end
		send "*"
		settextlinetrigger COMM2 :CONTINUEDAMAGE2 "Sub-space comm-link terminated"
		pause

		:CONTINUEDAMAGE2
	end
end
goto :WAIT_FOR_COMMAND

:CHECK_INVADE_MACRO_PARAMS
gosub :KILLTHETRIGGERS
setarray $SCAN_ARRAY 1000
gosub :QUIKSTATS
setvar $VALIDPROMPTS "Citadel Command"
gosub :CHECKSTARTINGPROMPT
if ($SHIP_MAX_ATTACK <= 0)
	gosub :GETSHIPSTATS
end
if ($PHOTONS <= 0)
	send "'{" $BOT_NAME "} - This command requires a photon*"
	goto :WAIT_FOR_COMMAND
end
isnumber $TEST $PARM2
if ((($TEST = FALSE) or ($PARM2 = 0)) and (($COMMAND <> "pe") and ($COMMAND <> "ped")))
	send "'{" $BOT_NAME "} - Parameter 2 invalid*"
	goto :WAIT_FOR_COMMAND
end
isnumber $TEST $PARM3
if (($TEST = FALSE) or ($PARM3 = 0))
	if ($COMMAND = "pxex")
		setvar $PARM3 $SHIP_NUMBER
	elseif (($COMMAND = "pxel") or ($COMMAND = "pxelk"))
		send "'{" $BOT_NAME "} - Planet Parameter in-valid*"
		goto :WAIT_FOR_COMMAND
	end
end
isnumber $TEST $PARM1
if ($TEST = FALSE)
	send "'{" $BOT_NAME "} - Sector Parameter in-valid*"
	goto :WAIT_FOR_COMMAND
end
if (($PARM1 > 10) and (($PARM1 <= SECTORS) and ($PARM1 <> STARDOCK)))
else
	send "'{" $BOT_NAME "} - Invalid attack sector entered*"
	goto :WAIT_FOR_COMMAND
end
setvar $I 1
setvar $ISFOUND FALSE
while (SECTOR.WARPS[$CURRENT_SECTOR][$I] > 0)
	if (SECTOR.WARPS[$CURRENT_SECTOR][$I] = $PARM1)
		setvar $ISFOUND TRUE
	end
	add $I 1
end
if ($ISFOUND = FALSE)
	send "'{" $BOT_NAME "} - Cannot continue.  Sector not Adjacent, aborting..*"
	goto :WAIT_FOR_COMMAND
end
getwordpos " "&$USER_COMMAND_LINE&" " $POS "speed"
if ($POS > 0)
	setvar $SPEED TRUE
else
	setvar $SPEED FALSE
end
send " c v * y * "&$PARM1&"*  "
if ($STARTINGLOCATION = "Citadel")
	send " q  q"
	gosub :GETPLANETINFO
	send "  C C  "
end
setvar $ENTER "m  "&$PARM1&"*"
setvar $XPORT "x   "&$PARM2&"*  q  z  n  "
setvar $PHOTON "  p y"&$PARM1&"*  q  "
return

:AUTOKILL

:KILL
gosub :KILLTHETRIGGERS
gosub :QUIKSTATS
setvar $STARTINGLOCATION $CURRENT_PROMPT
if ($STARTINGLOCATION <> "Command")
	if ($STARTINGLOCATION = "Citadel")
		if ($MODE <> "Citkill")
			setvar $USER_COMMAND_LINE "citkill on override"
			goto :RUNUSERCOMMANDLINE
		else
			setvar $USER_COMMAND_LINE "citkill off"
			goto :RUNUSERCOMMANDLINE
		end
	end
	setvar $MESSAGE "Wrong prompt for auto kill.*"
	gosub :SWITCHBOARD
	goto :WAIT_FOR_COMMAND
end
if ($SHIP_MAX_ATTACK <= 0)
	gosub :GETSHIPSTATS
end
gosub :GETSECTORDATA
gosub :FASTATTACK
goto :WAIT_FOR_COMMAND

:FASTATTACK
setvar $TARGETSTRING "a"
setvar $ISFOUND FALSE
getwordpos $SECTORDATA $BEACONPOS "[0m[35mBeacon  [1;33m:"

:CHECKINGFIGS
if ($FIGHTERS > 0)
	if ((($CURRENT_SECTOR > 10) and ($CURRENT_SECTOR <> STARDOCK)) and ($BEACONPOS > 0))
		setvar $TARGETSTRING $TARGETSTRING&"*"
	end
else
	gosub :QUIKSTATS
	if ($FIGHTERS <= 0)
		echo ANSI_12 "*You have no fighters.*" ANSI_7
		goto :STOPPINGPOINT
	else
		goto :CHECKINGFIGS
	end
end
if (($EMPTYSHIPCOUNT + ($FAKETRADERCOUNT + $REALTRADERCOUNT)) > 0)
	setvar $I 0
	while ($I < ($EMPTYSHIPCOUNT + $FAKETRADERCOUNT))
		setvar $TARGETSTRING $TARGETSTRING&"* "
		add $I 1
	end
	setvar $C 1
	while (($C <= $REALTRADERCOUNT) and ($ISFOUND = FALSE))
		if ($TRADERS[$C][1] = $CORP)
			setvar $TARGETSTRING $TARGETSTRING&"* "
		elseif ((($CURRENT_SECTOR <= 10) or ($CURRENT_SECTOR = STARDOCK)) and ($TRADERS[$C][2] = TRUE))
			setvar $TARGETSTRING $TARGETSTRING&"* "
		else
			setvar $ISFOUND TRUE
			setvar $TARGETSTRING $TARGETSTRING&"zy z"
		end
		add $C 1
	end
else
	setvar $MESSAGE "You have no targets.*"
	gosub :SWITCHBOARD
	goto :STOPPINGPOINT
end
if ($ISFOUND = TRUE)
	setvar $ATTACKSTRING ""
	while ($FIGHTERS > 0)
		if ($FIGHTERS < $SHIP_MAX_ATTACK)
			setvar $ATTACKSTRING $ATTACKSTRING&$TARGETSTRING&$FIGHTERS&"* * "
			setvar $FIGHTERS 0
		else
			setvar $ATTACKSTRING $ATTACKSTRING&$TARGETSTRING&$SHIP_MAX_ATTACK&"* * "
			setvar $FIGHTERS ($FIGHTERS - $SHIP_MAX_ATTACK)
		end
	end
else
	setvar $MESSAGE "You have no valid targets.*"
	gosub :SWITCHBOARD
	goto :STOPPINGPOINT
end
send $ATTACKSTRING&"* "
gosub :QUIKSTATS

:STOPPINGPOINT
return

:AUTOCAP

:CAP
gosub :KILLTHETRIGGERS
gosub :QUIKSTATS
setvar $STARTINGLOCATION $CURRENT_PROMPT
if ($STARTINGLOCATION <> "Command")
	if ($STARTINGLOCATION = "Citadel")
		if ($MODE <> "Citcap")
			fileexists $CAP_FILE_CHK $CAP_FILE
			if ($CAP_FILE_CHK <> TRUE)
				gosub :GETSHIPCAPSTATS
			end
			setvar $USER_COMMAND_LINE "citcap on"
			goto :RUNUSERCOMMANDLINE
		else
			setvar $USER_COMMAND_LINE "citcap off"
			goto :RUNUSERCOMMANDLINE
		end
	end
	setvar $MESSAGE "Wrong prompt for auto capture.*"
	gosub :SWITCHBOARD
	goto :WAIT_FOR_COMMAND
end
getwordpos $USER_COMMAND_LINE $POS "alien"
if ($POS > 0)
	setvar $ONLYALIENS TRUE
else
	setvar $ONLYALIENS FALSE
end
fileexists $CAP_FILE_CHK $CAP_FILE
if ($CAP_FILE_CHK <> TRUE)
	gosub :GETSHIPCAPSTATS
end
if ($SHIP_OFFENSIVE_ODDS <= 0)
	gosub :GETSHIPSTATS
end
setvar $LASTTARGET ""
setvar $THISTARGET ""
gosub :GETSECTORDATA
gosub :FASTCAPTURE
goto :WAIT_FOR_COMMAND

:FASTCAPTURE
setvar $ISFOUND FALSE
setvar $TARGETISALIEN FALSE
setvar $STILLSHIELDS FALSE
getwordpos $SECTORDATA $BEACONPOS "[0m[35mBeacon  [1;33m:"

:CHECKINGFIGS
if ($FIGHTERS > 0)
else
	gosub :QUIKSTATS
	if ($FIGHTERS <= 0)
		setvar $MESSAGE "No fighters on ship.*"
		gosub :SWITCHBOARD
		goto :CAPSTOPPINGPOINT
	else
		goto :CHECKINGFIGS
	end
end
if (($REALTRADERCOUNT > $CORPIECOUNT) and ($ONLYALIENS <> TRUE))
	setvar $TARGETSTRING "a "
	if ((($CURRENT_SECTOR > 10) and ($CURRENT_SECTOR <> $STARDOCK)) and ($BEACONPOS > 0))
		setvar $TARGETSTRING $TARGETSTRING&"* "
	end
	setvar $I 0
	while ($I < ($EMPTYSHIPCOUNT + $FAKETRADERCOUNT))
		setvar $TARGETSTRING $TARGETSTRING&"* "
		add $I 1
	end
	setvar $C 1
	while (($C <= $REALTRADERCOUNT) and ($ISFOUND = FALSE))
		if ((($CURRENT_SECTOR <= 10) or ($CURRENT_SECTOR = STARDOCK)) and ($TRADERS[$C][2] = TRUE))
			setvar $TARGETSTRING $TARGETSTRING&"* "
		elseif ($TRADERS[$C][1] = $CORP)
			setvar $TARGETSTRING $TARGETSTRING&"* "
		elseif (($TARGETINGCORP = TRUE) and ($TRADERS[$C][1] <> $TARGET))
			setvar $TARGETSTRING $TARGETSTRING&"* "
		elseif (($TARGETINGPERSON = TRUE) and ($TRADERS[$C] <> $TARGET))
			setvar $TARGETSTRING $TARGETSTRING&"* "
		else
			setvar $ISFOUND TRUE
			setvar $TARGETSTRING $TARGETSTRING&"zy z"
		end
		add $C 1
	end
end
if ((($FAKETRADERCOUNT > 0) and ($CAPPINGALIENS = TRUE)) and ($ISFOUND <> TRUE))
	setvar $TARGETSTRING "a "
	if ((($CURRENT_SECTOR > 10) and ($CURRENT_SECTOR <> $STARDOCK)) and ($BEACONPOS > 0))
		setvar $TARGETSTRING $TARGETSTRING&"* "
	end
	setvar $A 1
	while (($A <= $FAKETRADERCOUNT) and ($ISFOUND = FALSE))
		getwordpos $FAKETRADERS[$A] $POS "Zyrain"
		getwordpos $FAKETRADERS[$A] $POS2 "Clausewitz"
		getwordpos $FAKETRADERS[$A] $POS3 "Nelson"
		if (($POS <= 0) and (($POS2 <= 0) and ($POS3 <= 0)))
			setvar $I 0
			setvar $ISFOUND TRUE
			setvar $TARGETISALIEN TRUE
			setvar $TARGETSTRING $TARGETSTRING&"zy z"
		else
			setvar $TARGETSTRING $TARGETSTRING&"* "
		end
		add $A 1
	end
end
if (($ISFOUND = FALSE) and (($EMPTYSHIPCOUNT > 0) and (($CURRENT_SECTOR > 10) and ($CURRENT_SECTOR <> STARDOCK))))
	setvar $TARGETSTRING "a "
	if ((($CURRENT_SECTOR > 10) and ($CURRENT_SECTOR <> STARDOCK)) and ($BEACONPOS > 0))
		setvar $TARGETSTRING $TARGETSTRING&"* "
	end
	setvar $C 1
	setvar $ISFOUND FALSE
	while (($C <= $EMPTYSHIPCOUNT) and ($ISFOUND = FALSE))
		if (($EMPTYSHIPS[$C] = $CORP) or ($EMPTYSHIPS[$C] = $TRADER_NAME))
			setvar $TARGETSTRING $TARGETSTRING&"* "
		else
			setvar $ISFOUND TRUE
			setvar $TARGETSTRING $TARGETSTRING&"zy z"
		end
		add $C 1
	end
end
if ($ISFOUND = FALSE)
	setvar $MESSAGE "You have no targets.*"
	gosub :SWITCHBOARD
	goto :CAPSTOPPINGPOINT
else
	setvar $ATTACKSTRING ""

	:CAP_SHIP
	setvar $UNMANNED "NO"
	setvar $OWN_ODDS $SHIP_OFFENSIVE_ODDS
	setvar $CAP_POINTS 0
	setvar $MAX_FIGS 0
	setvar $CAP_SHIELD_POINTS 0
	setvar $SHIP_FIGHTERS 0
	setvar $LASTTARGET ""
	while ($FIGHTERS > 0)
		setvar $STILLSHIELDS FALSE
		setvar $ISSAMETARGET FALSE

		:CGOAHEAD
		settexttrigger FOUNDCAPTARGET :FOUNDCAPTARGET "(Y/N) [N]? Y"
		settextlinetrigger NOCTARGET :NOCAPPINGTARGETS "Do you want instructions (Y/N) [N]?"
		send $TARGETSTRING
		pause

		:FOUNDCAPTARGET
		killtrigger NOCTARGET
		killtrigger FOUNDCAPTARGET
		setvar $CAP_SHIP_INFO CURRENTLINE
		setvar $THISTARGET CURRENTANSILINE
		getword $CAP_SHIP_INFO $ATTACK_PROMPT 1
		if ($ATTACK_PROMPT <> "Attack")
			goto :WAIT_FOR_COMMAND
		end
		getwordpos $THISTARGET $POS "[0;33m([1;36m"
		cuttext $THISTARGET $THISTARGET 1 $POS
		if ($THISTARGET = $LASTTARGET)
			setvar $ISSAMETARGET TRUE
		elseif ($LASTTARGET = "")
			setvar $LASTTARGET $THISTARGET
		else
			goto :NOCAPPINGTARGETS
		end
		if ($ISSAMETARGET)
			goto :SEND_ATTACK
		end

		:SHIP_TYPE
		setvar $TYPE_COUNT 0
		setvar $IS_SHIP 0
		while ($TYPE_COUNT < $SHIPCOUNTER)
			add $TYPE_COUNT 1
			getwordpos $CAP_SHIP_INFO $IS_SHIP $SHIPLIST[$TYPE_COUNT]
			getwordpos $CAP_SHIP_INFO $UNMAN "'s unmanned"
			if ($UNMAN > 0)
				setvar $UNMANNED "YES"
			else
				setvar $UNMANNED "NO"
			end
			if (($IS_SHIP > 0) and ($SHIPLIST[$TYPE_COUNT] <> 0))
				getword $SHIP[$SHIPLIST[$TYPE_COUNT]] $SHIELDS 1
				getword $SHIP[$SHIPLIST[$TYPE_COUNT]] $DEFODDS 2
				goto :SEND_ATTACK
			end
		end
		setvar $MESSAGE "Unknown ship type, cannot calculate attack, you must do it manually.*"
		gosub :SWITCHBOARD
		send "* "
		return

		:SEND_ATTACK
		gosub :KILLTHETRIGGERS
		gettext $CAP_SHIP_INFO $SHIP_FIGHTERS $SHIPLIST[$TYPE_COUNT] "(Y/N)"
		gettext $SHIP_FIGHTERS $SHIP_FIGHTERS "-" ")"
		striptext $SHIP_FIGHTERS ","
		setvar $SHIP_SHIELD_PERCENT 0
		setvar $SHIELDPOINTS 0
		settextlinetrigger COMBAT :COMBAT_SCAN "Combat scanners show enemy shields at"
		settexttrigger NOCOMBAT :CAP_IT "How many fighters do you wish to use"
		settextlinetrigger NOTARGET :NOCAPPINGTARGETS "Do you want instructions (Y/N) [N]?"
		pause

		:COMBAT_SCAN
		getword CURRENTLINE $SHIELDPERC 7
		striptext $SHIELDPERC "%"
		setvar $SHIELDPOINTS (($SHIELDS * $SHIELDPERC) / 100)
		setvar $STILLSHIELDS TRUE
		pause

		:CAP_IT
		killtrigger COMBAT_SCAN
		killtrigger CAP_IT
		killtrigger NOTARGET
		getword CURRENTLINE $MAX_FIGS 11
		striptext $MAX_FIGS ","
		striptext $MAX_FIGS ")"
		setvar $CAP_POINTS (($SHIELDPOINTS + $SHIP_FIGHTERS) * $DEFODDS)
		if ((($DEFENDERCAPPING = TRUE) and ($UNMANNED <> "YES")) and ($TARGETISALIEN = TRUE))
			if ($SHIP_FIGHTERS > 100)
				setvar $FIGBUFFER (($SHIP_FIGHTERS * 2) / 100)
			else
				setvar $FIGBUFFER 0
			end
			if ($STILLSHIELDS = TRUE)
				setvar $CAP_POINTS ($CAP_POINTS / $OWN_ODDS)
			else
				setvar $CAP_POINTS 1
			end
		else
			setvar $CAP_POINTS (($CAP_POINTS / $OWN_ODDS) - ($CAP_POINTS / 100))
		end
		setvar $CAP_POINTS (($CAP_POINTS * 95) / 100)
		if ($UNMANNED = "YES")
			divide $CAP_POINTS 2
		end
		if ($CAP_POINTS <= 0)
			setvar $CAP_POINTS 1
		elseif ($CAP_POINTS > $MAX_FIGS)
			setvar $CAP_POINTS $MAX_FIGS
		end
		setvar $SENDATTACK $CAP_POINTS&"*"
		if ($STARTINGLOCATION = "Citadel")
			setvar $SENDATTACK $SENDATTACK&$REFURBSTRING
		end
		send $SENDATTACK
		setvar $FIGHTERS ($FIGHTERS - $CAP_POINTS)

		:KEEPCAPPING
	end
end
goto :CAPSTOPPINGPOINT

:NOCAPPINGTARGETS
killtrigger NOCTARGET
killtrigger FOUNDCAPTARGET
send "* "

:CAPSTOPPINGPOINT
return

:SCRUB
setvar $SCRUBONLY TRUE

:AUTOREFURB

:REFURB
gosub :KILLTHETRIGGERS
if ($SELF_COMMAND <> TRUE)
	gosub :QUIKSTATS
end
getword CURRENTLINE $STARTINGLOCATION 1
if (($STARTINGLOCATION <> "Command") and ($STARTINGLOCATION <> "Citadel"))
	gosub :CURRENT_PROMPT
	setvar $VALIDPROMPTS "Citadel Command"
	gosub :CHECKSTARTINGPROMPT
end
if (CURRENTSECTOR = STARDOCK)
	send "p ss ys *p"
elseif ((CURRENTSECTOR = 1) or (PORT.CLASS[CURRENTSECTOR] = 0))
	if ($STARTINGLOCATION = "Citadel")
		send "q "
		gosub :GETPLANETINFO
		send "q "
	end
	send "p ty"
else
	setvar $MESSAGE "No known class 0 or 9 port here to refurb at.*"
	gosub :SWITCHBOARD
	goto :WAIT_FOR_COMMAND
end
setvar $MESSAGE ""
settextlinetrigger LIMPET :MARKLIMPET "After an intensive scanning search, they find and remove the Limpet"
settextlinetrigger LIMPETNO :MARKLIMPETNO "The port official frowns at you (you haven't the funds!) and storms"
settextlinetrigger FIGHTER :BUYFIGHTERS "B  Fighters        :"
pause

:MARKLIMPET
setvar $MESSAGE "Limpet scrubbed off of hull.*"
pause

:MARKLIMPETNO
setvar $MESSAGE "Limpet exists, but not enough cash to get scrubbed.*"
pause

:BUYFIGHTERS
gosub :KILLTHETRIGGERS
if ($SCRUBONLY = FALSE)
	getword CURRENTLINE $FIGSTOBUY 8
	waiton " credits per point "
	getword CURRENTLINE $SHIELDSTOBUY 9
	send "b "&$FIGSTOBUY&"* c "&$SHIELDSTOBUY&"* q q q * "
else
	send "b 0* c 0* q q q * "
end
if ($STARTINGLOCATION = "Citadel")
	gosub :LANDINGSUB
end
gosub :QUIKSTATS
if ($MESSAGE <> "")
	gosub :SWITCHBOARD
end
goto :WAIT_FOR_COMMAND

:SS
cuttext $USER_COMMAND_LINE $USER_COMMAND_LINE 2 9999
send "'"&$USER_COMMAND_LINE&"*"
goto :WAIT_FOR_COMMAND

:FED
cuttext $USER_COMMAND_LINE $USER_COMMAND_LINE 2 9999
send "`"&$USER_COMMAND_LINE&"*"
goto :WAIT_FOR_COMMAND

:ABOUT
gosub :DOSPLASHSCREEN
echo "*" CURRENTANSILINE
goto :WAIT_FOR_COMMAND

:BOT
setvar $MESSAGE ""
if ($PARM1 = "on")
	setvar $BOTISOFF FALSE
	setvar $MESSAGE "Bot Active*"
end
if ($PARM1 = "off")
	setvar $BOTISOFF TRUE
	setvar $MESSAGE "Bot Deactivated*"
end
if (($PARM1 <> "off") and ($PARM1 <> "on"))
	setvar $MESSAGE "That status option is unknown..*"
end
gosub :SWITCHBOARD
goto :WAIT_FOR_COMMAND

:RELOG
setvar $MESSAGE ""
if ($PARM1 = "on")
	setvar $MESSAGE "Relog Active*"
	setvar $DORELOG TRUE
end
if ($PARM1 = "off")
	setvar $MESSAGE "Relog Deactivated*"
	setvar $DORELOG FALSE
end
if (($PARM1 <> "off") and ($PARM1 <> "on"))
	setvar $MESSAGE "Please use relog [on/off] format.*"
	goto :WAIT_FOR_COMMAND
end
savevar $DORELOG
gosub :SWITCHBOARD
goto :WAIT_FOR_COMMAND

:REFRESH
gosub :QUIKSTATS
setvar $VALIDPROMPTS "Citadel Command"
gosub :CHECKSTARTINGPROMPT
gosub :GETINFO
gosub :GETSHIPSTATS
fileexists $CAP_FILE_CHK $CAP_FILE
if ($CAP_FILE_CHK)
	gosub :LOADSHIPINFO
else
	gosub :GETSHIPCAPSTATS
	gosub :LOADSHIPINFO
end
send "'{"&$BOT_NAME&"} - Bot data refresh completed.*"
goto :WAIT_FOR_COMMAND

:LOADSHIPINFO
setvar $SHIPCOUNTER 1

:READSHIPLIST
read $CAP_FILE $SHIPINF $SHIPCOUNTER
if ($SHIPINF <> EOF)
	getword $SHIPINF $SHIELDS 1
	getlength $SHIELDS $SHIELDLEN
	getword $SHIPINF $DEFODD 2
	getlength $DEFODD $DEFODDLEN
	getword $SHIPINF $OFF_ODDS 3
	getlength $OFF_ODDS $FILLER1LEN
	getword $SHIPINF $SHIP_COST 4
	getlength $SHIP_COST $FILLER2LEN
	getword $SHIPINF $MAX_HOLDS 5
	getlength $MAX_HOLDS $FILLER3LEN
	getword $SHIPINF $MAX_FIGHTERS 6
	getlength $MAX_FIGHTERS $FILLER4LEN
	getword $SHIPINF $INIT_HOLDS 7
	getlength $INIT_HOLDS $FILLER5LEN
	getword $SHIPINF $TPW 8
	getlength $TPW $FILLER6LEN
	getword $SHIPINF $ISDEFENDER 9
	getlength $ISDEFENDER $FILLER7LEN
	setvar $STARTLEN ($SHIELDLEN + ($DEFODDLEN + ($FILLER1LEN + ($FILLER2LEN + ($FILLER3LEN + ($FILLER4LEN + ($FILLER5LEN + ($FILLER6LEN + ($FILLER7LEN + 10)))))))))
	cuttext $SHIPINF $SHIPNAME $STARTLEN 999
	setvar $SHIP[$SHIPNAME] $SHIELDS&" "&$DEFODD
	setvar $SHIPLIST[$SHIPCOUNTER] $SHIPNAME
	setvar $SHIPLIST[$SHIPCOUNTER][1] $SHIELDS
	setvar $SHIPLIST[$SHIPCOUNTER][2] $DEFODD
	setvar $SHIPLIST[$SHIPCOUNTER][3] $OFF_ODDS
	setvar $SHIPLIST[$SHIPCOUNTER][4] $MAX_HOLDS
	setvar $SHIPLIST[$SHIPCOUNTER][5] $MAX_FIGHTERS
	setvar $SHIPLIST[$SHIPCOUNTER][6] $INIT_HOLDS
	setvar $SHIPLIST[$SHIPCOUNTER][7] $TPW
	setvar $SHIPLIST[$SHIPCOUNTER][8] $ISDEFENDER
	setvar $SHIPLIST[$SHIPCOUNTER][9] $SHIP_COST
	add $SHIPCOUNTER 1
	goto :READSHIPLIST
end
setvar $SHIPSTATS TRUE
return

:GETSHIPCAPSTATS
send "cn"
waiton "(2) Animation display"
getword CURRENTLINE $ANSI_ONOFF 5
if ($ANSI_ONOFF = "On")
	send "2qq"
else
	send "qq"
end
setarray $ALPHA 20
delete $CAP_FILE
setvar $ALPHA[1] "A"
setvar $ALPHA[2] "B"
setvar $ALPHA[3] "C"
setvar $ALPHA[4] "D"
setvar $ALPHA[5] "E"
setvar $ALPHA[6] "F"
setvar $ALPHA[7] "G"
setvar $ALPHA[8] "H"
setvar $ALPHA[9] "I"
setvar $ALPHA[10] "J"
setvar $ALPHA[11] "K"
setvar $ALPHA[12] "L"
setvar $ALPHA[13] "M"
setvar $ALPHA[14] "N"
setvar $ALPHA[15] "O"
setvar $ALPHA[16] "P"
setvar $ALPHA[17] "R"
setvar $ALPHALOOP 0
setvar $TOTALSHIPS 0
setvar $NEXTPAGE 1
send "CC?"
waiton "(?=List) ?"

:SHP_LOOP
settextlinetrigger GRAB_SHIP :SHP_SHIPNAMES
pause

:SHP_SHIPNAMES
if (CURRENTLINE = "")
	goto :SHP_LOOP
end
getword CURRENTLINE $STOPPER 1
if ($STOPPER = "<+>")
	send "+"
	waiton "(?=List) ?"
	setvar $NEXTPAGE 1
	goto :SHP_LOOP
elseif ($STOPPER = "<Q>")
	goto :SHP_GETSHIPSTATS
end
if ($NEXTPAGE = 1)
	setvar $SHIPNAME CURRENTLINE
	striptext $SHIPNAME "<A> "
	if ($SHIPNAME = $FIRSTSHIPNAME)
		goto :SHP_GETSHIPSTATS
	end
	setvar $NEXTPAGE 0
end
add $TOTALSHIPS 1
if ($TOTALSHIPS = 1)
	setvar $FIRSTSHIPNAME CURRENTLINE
	striptext $FIRSTSHIPNAME "<A> "
end
goto :SHP_LOOP

:SHP_GETSHIPSTATS
setvar $SHIPSTATLOOP 0

:SHP_SHIPSTATS
while ($SHIPSTATLOOP < $TOTALSHIPS)
	add $SHIPSTATLOOP 1
	add $ALPHALOOP 1
	if ($ALPHALOOP > 17)
		send "+"
		setvar $ALPHALOOP 1
	end
	send $ALPHA[$ALPHALOOP]
	settextlinetrigger SN :SN "Ship Class :"
	pause

	:SN
	setvar $LINE CURRENTLINE
	getwordpos $LINE $POS ":"
	add $POS 2
	cuttext $LINE $SHIP_NAME $POS 999
	settextlinetrigger HC :HC "Basic Hold Cost:"
	pause

	:HC
	setvar $LINE CURRENTLINE
	striptext $LINE "Basic Hold Cost:"
	striptext $LINE "Initial Holds:"
	striptext $LINE "Maximum Shields:"
	getword $LINE $INIT_HOLDS 2
	getword $LINE $MAX_SHIELDS 3
	striptext $MAX_SHIELDS ","
	settextlinetrigger OO :OO2 "Offensive Odds:"
	pause

	:OO2
	setvar $LINE CURRENTLINE
	striptext $LINE "Main Drive Cost:"
	striptext $LINE "Max Fighters:"
	striptext $LINE "Offensive Odds:"
	getword $LINE $MAX_FIGS 2
	getword $LINE $OFF_ODDS 3
	striptext $MAX_FIGS ","
	striptext $OFF_ODDS ":1"
	striptext $OFF_ODDS "."
	settextlinetrigger DO :DO "Defensive Odds:"
	pause

	:DO
	setvar $LINE CURRENTLINE
	striptext $LINE "Computer Cost:"
	striptext $LINE "Turns Per Warp:"
	striptext $LINE "Defensive Odds:"
	getword $LINE $DEF_ODDS 3
	striptext $DEF_ODDS ":1"
	striptext $DEF_ODDS "."
	getword $LINE $TPW 2
	settextlinetrigger SC :SC "Ship Base Cost:"
	pause

	:SC
	setvar $LINE CURRENTLINE
	striptext $LINE "Ship Base Cost:"
	getword $LINE $COST 1
	striptext $COST ","
	getlength $COST $COSTLEN
	if ($COSTLEN = 7)
		add $COST 10000000
	end
	settextlinetrigger MH :MH "Maximum Holds:"
	pause

	:MH
	setvar $LINE CURRENTLINE
	striptext $LINE "Maximum Holds:"
	getword $LINE $MAX_HOLDS 1
	setvar $ISDEFENDER FALSE
	write $CAP_FILE $MAX_SHIELDS&" "&$DEF_ODDS&" "&$OFF_ODDS&" "&$COST&" "&$MAX_HOLDS&" "&$MAX_FIGS&" "&$INIT_HOLDS&" "&$TPW&" "&$ISDEFENDER&" "&$SHIP_NAME
end
send "qq"
return

:SETPLANETNUMBER
getwordpos RAWPACKET $POS "Planet "&#27&"[1;33m#"&#27&"[36m"
if ($POS > 0)
	gettext RAWPACKET $PLANET "Planet "&#27&"[1;33m#"&#27&"[36m" #27&"[0;32m in sector "
	savevar $PLANET
end
settextlinetrigger GETPLANETNUMBER :SETPLANETNUMBER "Planet #"
pause

:FEDERASEFIG
getword CURRENTLINE $SPOOF 1
if ($SPOOF <> "The")
	goto :ENDFEDERASEFIG
end
gettext CURRENTLINE&"  [XX][XX][XX]" $TEMP " fighters in sector " ". [XX][XX][XX]"
if ($TEMP <> "")
	isnumber $TEST $TEMP
	if ($TEST = TRUE)
		if (($TEMP <= SECTORS) and ($TEMP > 0))
			setvar $TARGET $TEMP
			gosub :REMOVEFIGFROMDATA
		end
	end
end

:ENDFEDERASEFIG
settextlinetrigger FEDERASE :FEDERASEFIG "The Federation We destroyed "
pause

:ERASEFIG
cuttext CURRENTLINE&"     " $SPOOF 1 2
cuttext CURRENTLINE&"     " $SPOOF2 1 1
if (($SPOOF = "R ") or ($SPOOF = "F ") or ($SPOOF = "P ") or ($SPOOF2 = "'") or ($SPOOF2 = "`"))
	goto :ENDERASEFIG
end
gettext CURRENTLINE&" [XX][XX][XX]" $TEMP " destroyed " " [XX][XX][XX]"
if ($TEMP <> "")
	getword $TEMP $FIG_HIT 7
	getword $TEMP $FIG_NUMBER 1
	isnumber $TEST $FIG_HIT
	if (($TEST = TRUE) and ($FIG_NUMBER <> 0))
		if (($FIG_HIT <= SECTORS) and ($FIG_HIT > 0))
			setvar $TARGET $FIG_HIT
			gosub :REMOVEFIGFROMDATA
		end
	end
end

:ENDERASEFIG
settextlinetrigger FIGHTERSERASE :ERASEFIG " of your fighters in sector "
pause

:ERASEWARPFIG
getword CURRENTLINE $SPOOF 1
if ($SPOOF <> "You")
	settextlinetrigger WARPFIGERASE :ERASEWARPFIG "You do not have any fighters in Sector "
	pause
end
gettext CURRENTLINE&" [XX][XX][XX]" $TEMP "You do not have any fighters in Sector " ". [XX][XX][XX]"
if ($TEMP <> "")
	isnumber $TEST $TEMP
	if ($TEST)
		if (($TEMP <= SECTORS) and ($TEMP > 0))
			setvar $TARGET $TEMP
			gosub :REMOVEFIGFROMDATA
		end
	end
end
settextlinetrigger WARPFIGERASE :ERASEWARPFIG "You do not have any fighters in Sector "
pause

:ADDFIG
isnumber $TEST CURRENTSECTOR
if ($TEST)
	if ((CURRENTSECTOR > 10) and (CURRENTSECTOR < SECTORS))
		setvar $TARGET CURRENTSECTOR
		gosub :ADDFIGTODATA
	end
end
settextlinetrigger FIGHTERSADD :ADDFIG "Should they be (D)efensive, (O)ffensive or Charge a (T)oll ?"
pause

:ERASEBUSTS
cuttext CURRENTLINE&"   " $SPOOF 1 1
if ($SPOOF <> "R")
	settextlinetrigger CLEARBUSTS :ERASEBUSTS ">[Busted:"
	pause
end
gettext CURRENTLINE&" [XX][XX][XX]" $TEMP ">[Busted:" "]<"
if ($TEMP <> "")
	isnumber $TEST $TEMP
	if ($TEST)
		if (($TEMP <= SECTORS) and ($TEMP > 0))
			setsectorparameter $TEMP "BUSTED" FALSE
		end
	end
end
settextlinetrigger CLEARBUSTS :ERASEBUSTS ">[Busted:"
pause

:CHECKSECTORDATA
gettext CURRENTLINE $CURSEC "]:[" "] ("
if ($CURSEC = CURRENTSECTOR)
	setvar $CURRENT_SECTOR $CURSEC
	getsectorparameter $CURRENT_SECTOR "BUSTED" $ISBUSTED
	if ($ISBUSTED)
		echo ANSI_5 "[" ANSI_12 "B" ANSI_5 "] : "
	end
	getsectorparameter $CURRENT_SECTOR "MSLSEC" $ISMSL
	if ($ISMSL)
		echo ANSI_5 "[" ANSI_9 "MSL" ANSI_5 "] : "
	end
end
settexttrigger SECTORDATA :CHECKSECTORDATA "(?=Help)? :"
pause

:SETSHIPOFFENSIVEODDS
getwordpos CURRENTANSILINE $POS "[0;31m:[1;36m1"
if ($POS > 0)
	gettext CURRENTANSILINE $SHIP_OFFENSIVE_ODDS "Offensive Odds[1;33m:[36m " "[0;31m:[1;36m1"
	striptext $SHIP_OFFENSIVE_ODDS "."
	striptext $SHIP_OFFENSIVE_ODDS " "
	gettext CURRENTANSILINE $SHIP_FIGHTERS_MAX "Max Fighters[1;33m:[36m" "[0;32m Offensive Odds"
	striptext $SHIP_FIGHTERS_MAX ","
	striptext $SHIP_FIGHTERS_MAX " "
end
settextlinetrigger GETSHIPSTATS :SETSHIPOFFENSIVEODDS "Offensive Odds: "
pause

:SETSHIPMAXFIGATTACK
getwordpos CURRENTANSILINE $POS "[0m[32m Max Figs Per Attack[1;33m:[36m"
if ($POS > 0)
	gettext CURRENTANSILINE $SHIP_MAX_ATTACK "[0m[32m Max Figs Per Attack[1;33m:[36m" "[0;32mTransWarp"
	striptext $SHIP_MAX_ATTACK " "
end
settextlinetrigger GETSHIPMAXFIGHTERS :SETSHIPMAXFIGATTACK " TransWarp Drive:   "
pause

:CN

:CN9
gosub :CURRENT_PROMPT
setvar $VALIDPROMPTS "Citadel Command Computer"
gosub :CHECKSTARTINGPROMPT
if ($STARTINGLOCATION = "Computer")
	send "q"
end
gosub :STARTCNSETTINGS
setvar $MESSAGE "CN Settings are reset for this bot.*"
gosub :SWITCHBOARD
goto :WAIT_FOR_COMMAND

:STARTCNSETTINGS
send "CN"
settextlinetrigger ANSI1 :CNCHECK "(1) ANSI graphics            - Off"
settextlinetrigger ANIM1 :CNCHECK "(2) Animation display        - On"
settextlinetrigger PAGE1 :CNCHECK "(3) Page on messages         - On"
settextlinetrigger SETSSCHN :SETSSCHN "(4) Sub-space radio channel"
settextlinetrigger SILENCE1 :CNCHECK "(7) Silence ALL messages     - Yes"
settextlinetrigger ABORTDISPLAY1 :CNCHECK "(9) Abort display on keys    - ALL KEYS"
settextlinetrigger MESSAGEDISPLAY1 :CNCHECK "(A) Message Display Mode     - Long"
settextlinetrigger SCREENPAUSES1 :CNCHECK "(B) Screen Pauses            - Yes"
settextlinetrigger ONLINEAUTOFLEE0 :CNCDONE "(C) Online Auto Flee         - Off"
settextlinetrigger ONLINEAUTOFLEE1 :CNCALMOSTDONE "(C) Online Auto Flee         - On"
pause

:CNCHECK
gosub :GETCNC
pause

:SETSSCHN
getword CURRENTLINE $SUBSPACE 6
if ($SUBSPACE = 0)
	getrnd $SUBSPACE 101 60000
	send 4&$SUBSPACE&"*"
end
savevar $SUBSPACE
pause

:CNCALMOSTDONE
gosub :GETCNC

:CNCDONE
send "QQ"
settexttrigger SUBSTARTCNCONTINUE1 :SUBSTARTCNCONTINUE "Command [TL="
settexttrigger SUBSTARTCNCONTINUE2 :SUBSTARTCNCONTINUE "Citadel command (?=help)"
pause

:SUBSTARTCNCONTINUE
gosub :KILLTHETRIGGERS
return

:GETCNC
getword CURRENTLINE $CNC 1
striptext $CNC "("
striptext $CNC ")"
send $CNC&"  "
return

:GAMESTATS
if ($STARTINGLOCATION = "Citadel")
	send "qqzn"
end
if (($STARTINGLOCATION = "Command") or ($STARTINGLOCATION = "Citadel"))
	send "qyn"
	waitfor "Enter your choice"
	send #42 "*"
	settextlinetrigger SETTINGS1 :FINDGOLD "Gold Enabled="
	settextlinetrigger SETTINGS2 :FINDMBBS "MBBS Compatibility="
	settextlinetrigger SETTINGS3 :FINDALIENS "Internal Aliens="
	settextlinetrigger SETTINGS4 :FINDFERRENGI "Internal Ferrengi="
	settextlinetrigger SETTINGS5 :FINDMAXCOMMANDS "Max Commands="
	settextlinetrigger SETTINGS6 :FINDINACTIVE "Inactive Time="
	settextlinetrigger SETTINGS7 :FINDCOLOREGEN "Colonist Regen Rate="
	settextlinetrigger SETTINGS8 :FINDPHOTONDUR "Photon Missile Duration="
	settextlinetrigger SETTINGS9 :FINDDEBRIS "Debris Loss Percent="
	settextlinetrigger SETTINGS10 :FINDTRADEPERCENT "Trade Percent="
	settextlinetrigger SETTINGS11 :FINDPRODUCTIONRATE "Production Rate="
	settextlinetrigger SETTINGS12 :FINDMAXPRODUCTIONRATE "Max Production Regen="
	settextlinetrigger SETTINGS13 :FINDMULTIPLEPHOTONS "Multiple Photons="
	settextlinetrigger SETTINGS14 :FINDCLEARBUSTS "Clear Bust Days="
	settextlinetrigger SETTINGS15 :FINDSTEALFACTOR "Steal Factor="
	settextlinetrigger SETTINGS16 :FINDROBFACTOR "Rob Factor="
	settextlinetrigger SETTINGS17 :FINDPORTMAX "Port Production Max="
	settextlinetrigger SETTINGS18 :FINDRADIATION "Radiation Lifetime="
	settextlinetrigger REREGISTER :REREGISTER "Reregister Ship="
	settextlinetrigger SETTINGS37 :FINDLIMPETREMOVAL "Limpet Removal="
	settextlinetrigger SETTINGS20 :FINDGENESIS "Genesis Torpedo="
	settextlinetrigger SETTINGS21 :FINDARMID "Armid Mine="
	settextlinetrigger SETTINGS22 :FINDLIMPET "Limpet Mine="
	settextlinetrigger SETTINGS23 :FINDBEACON "Beacon="
	settextlinetrigger SETTINGS24 :FINDTWARPI "Type I TWarp="
	settextlinetrigger SETTINGS25 :FINDTWARPII "Type II TWarp="
	settextlinetrigger SETTINGS26 :FINDTWARPUPGRADE "TWarp Upgrade="
	settextlinetrigger SETTINGS27 :FINDPSYCHIC "Psychic Probe="
	settextlinetrigger SETTINGS28 :FINDPLANETSCANNER "Planet Scanner="
	settextlinetrigger SETTINGS29 :FINDATOMIC "Atomic Detonator="
	settextlinetrigger SETTINGS30 :FINDCORBO "Corbomite="
	settextlinetrigger SETTINGS31 :FINDETHER "Ether Probe="
	settextlinetrigger SETTINGS32 :FINDPHOTON "Photon Missile="
	settextlinetrigger SETTINGS33 :FINDCLOAK "Cloaking Device="
	settextlinetrigger SETTINGS34 :FINDDISRUPTOR "Mine Disruptor="
	settextlinetrigger SETTINGS35 :FINDHOLOSCANNER "Holographic Scanner="
	settextlinetrigger SETTINGS36 :FINDDENSITYSCAN "Density Scanner="
	settextlinetrigger SETTINGS38 :FINDMAXPLANETS "Max Planet Sector="
	pause

	:FINDGOLD
	getword CURRENTLINE $CHECK 2
	striptext $CHECK "Enabled="
	if ($CHECK = "True")
		setvar $GOLDENABLED TRUE
		savevar $GOLDENABLED
	else
		setvar $GOLDENABLED FALSE
		savevar $GOLDENABLED
	end
	pause

	:FINDMAXPLANETS
	getword CURRENTLINE $CHECK 3
	striptext $CHECK "Sector="
	setvar $MAX_PLANETS_PER_SECTOR $CHECK
	savevar $MAX_PLANETS_PER_SECTOR
	pause

	:FINDMBBS
	getword CURRENTLINE $MBBS_CK 2
	striptext $MBBS_CK "Compatibility="
	if ($MBBS_CK = "True")
		setvar $MBBS TRUE
		savevar $MBBS
	elseif ($MBBS_CK = "False")
		setvar $MBBS FALSE
		savevar $MBBS
	end
	pause

	:FINDALIENS
	getword CURRENTLINE $CHECK 2
	striptext $CHECK "Aliens="
	if ($CHECK = "True")
		setvar $INTERNALALIENS TRUE
		savevar $INTERNALALIENS
	elseif ($CHECK = "False")
		setvar $INTERNALALIENS FALSE
		savevar $INTERNALALIENS
	end
	pause

	:FINDFERRENGI
	getword CURRENTLINE $CHECK 2
	striptext $CHECK "Ferrengi="
	if ($CHECK = "True")
		setvar $INTERNALFERRENGI TRUE
		savevar $INTERNALFERRENGI
	elseif ($CHECK = "False")
		setvar $INTERNALFERRENGI FALSE
		savevar $INTERNALFERRENGI
	end
	pause

	:FINDMAXCOMMANDS
	getword CURRENTLINE $CHECK 2
	striptext $CHECK "Commands="
	setvar $MAX_COMMANDS $CHECK
	savevar $MAX_COMMANDS
	pause

	:FINDINACTIVE
	getword CURRENTLINE $CHECK 2
	striptext $CHECK "Time="
	setvar $INACTIVE_TIME $CHECK
	savevar $INACTIVE_TIME
	pause

	:FINDCOLOREGEN
	setvar $LINE CURRENTLINE
	striptext $LINE "Colonist Regen Rate="
	striptext $LINE ","
	lowercase $LINE
	replacetext $LINE "m" 000000
	replacetext $LINE "k" 000
	setvar $COLONIST_REGEN $LINE
	savevar $COLONIST_REGEN
	pause

	:FINDPHOTONDUR
	getword CURRENTLINE $CHECK 3
	striptext $CHECK "Duration="
	setvar $PHOTON_DURATION $CHECK
	savevar $PHOTON_DURATION
	if ($PHOTON_DURATION <= 0)
		setvar $PHOTONS_ENABLED FALSE
	else
		setvar $PHOTONS_ENABLED TRUE
	end
	savevar $PHOTONS_ENABLED
	pause

	:FINDDEBRIS
	getword CURRENTLINE $CHECK 3
	striptext $CHECK "Percent="
	striptext $CHECK "%"
	setvar $DEBRIS_LOSS $CHECK
	savevar $DEBRIS_LOSS
	pause

	:FINDTRADEPERCENT
	getword CURRENTLINE $PTRADESETTING 2
	striptext $PTRADESETTING "Percent="
	striptext $PTRADESETTING "%"
	savevar $PTRADESETTING
	pause

	:FINDPRODUCTIONRATE
	getword CURRENTLINE $PRODUCTION_RATE 2
	striptext $PRODUCTION_RATE "Rate="
	savevar $PRODUCTION_RATE
	pause

	:FINDMAXPRODUCTIONRATE
	getword CURRENTLINE $PRODUCTION_REGEN 3
	striptext $PRODUCTION_REGEN "Regen="
	savevar $PRODUCTION_REGEN
	pause

	:FINDMULTIPLEPHOTONS
	getword CURRENTLINE $MULTIPLE_PHOTONS 2
	striptext $MULTIPLE_PHOTONS "Photons="
	savevar $MULTIPLE_PHOTONS
	pause

	:FINDCLEARBUSTS
	getword CURRENTLINE $CLEAR_BUST_DAYS 3
	striptext $CLEAR_BUST_DAYS "Days="
	savevar $CLEAR_BUST_DAYS
	pause

	:FINDSTEALFACTOR
	getword CURRENTLINE $STEAL_FACTOR 2
	striptext $STEAL_FACTOR "Factor="
	striptext $STEAL_FACTOR "%"
	savevar $STEAL_FACTOR
	pause

	:FINDROBFACTOR
	getword CURRENTLINE $ROB_FACTOR 2
	striptext $ROB_FACTOR "Factor="
	striptext $ROB_FACTOR "%"
	savevar $ROB_FACTOR
	pause

	:FINDPORTMAX
	setvar $LINE CURRENTLINE
	striptext $LINE "Port Production Max="
	setvar $PORT_MAX $LINE
	savevar $PORT_MAX
	pause

	:FINDRADIATION
	getword CURRENTLINE $RADIATION_LIFETIME 2
	striptext $RADIATION_LIFETIME "Lifetime="
	savevar $RADIATION_LIFETIME
	pause

	:FINDLIMPETREMOVAL
	getword CURRENTLINE $LIMPET_REMOVAL_COST 2
	striptext $LIMPET_REMOVAL_COST "Removal="
	striptext $LIMPET_REMOVAL_COST ","
	striptext $LIMPET_REMOVAL_COST "$"
	savevar $LIMPET_REMOVAL_COST
	setvar $LSD_LIMPREMOVALCOST $LIMPET_REMOVAL_COST
	savevar $LSD_LIMPREMOVALCOST
	pause

	:FINDGENESIS
	getword CURRENTLINE $GENESIS_COST 2
	striptext $GENESIS_COST "Torpedo="
	striptext $GENESIS_COST ","
	striptext $GENESIS_COST "$"
	savevar $GENESIS_COST
	setvar $LSD_GENCOST $GENESIS_COST
	savevar $LSD_GENCOST
	pause

	:FINDARMID
	getword CURRENTLINE $ARMID_COST 2
	striptext $ARMID_COST "Mine="
	striptext $ARMID_COST ","
	striptext $ARMID_COST "$"
	savevar $ARMID_COST
	setvar $LSD_ARMIDCOST $ARMID_COST
	savevar $LSD_ARMIDCOST
	pause

	:FINDLIMPET
	getword CURRENTLINE $LIMPET_COST 2
	striptext $LIMPET_COST "Mine="
	striptext $LIMPET_COST ","
	striptext $LIMPET_COST "$"
	savevar $LIMPET_COST
	setvar $LSD_LIMPCOST $LIMPET_COST
	savevar $LSD_LIMPCOST
	pause

	:FINDBEACON
	getword CURRENTLINE $BEACON_COST 1
	striptext $BEACON_COST "Beacon="
	striptext $BEACON_COST ","
	striptext $BEACON_COST "$"
	savevar $BEACON_COST
	setvar $LSD_BEACON $BEACON_COST
	savevar $LSD_BEACON
	pause

	:FINDTWARPI
	getword CURRENTLINE $TWARPI_COST 3
	striptext $TWARPI_COST "TWarp="
	striptext $TWARPI_COST ","
	striptext $TWARPI_COST "$"
	savevar $TWARPI_COST
	setvar $LSD_TWARPICOST $TWARPI_COST
	savevar $LSD_TWARPICOST
	pause

	:FINDTWARPII
	getword CURRENTLINE $TWARPII_COST 3
	striptext $TWARPII_COST "TWarp="
	striptext $TWARPII_COST ","
	striptext $TWARPII_COST "$"
	savevar $TWARPII_COST
	setvar $LSD_TWARPIICOST $TWARPII_COST
	savevar $LSD_TWARPIICOST
	pause

	:FINDTWARPUPGRADE
	getword CURRENTLINE $TWARP_UPGRADE_COST 2
	striptext $TWARP_UPGRADE_COST "Upgrade="
	striptext $TWARP_UPGRADE_COST ","
	striptext $TWARP_UPGRADE_COST "$"
	savevar $TWARP_UPGRADE_COST
	setvar $LSD_TWARPUPCOST $TWARP_UPGRADE_COST
	savevar $LSD_TWARPUPCOST
	pause

	:FINDPSYCHIC
	getword CURRENTLINE $PSYCHIC_COST 2
	striptext $PSYCHIC_COST "Probe="
	striptext $PSYCHIC_COST ","
	striptext $PSYCHIC_COST "$"
	savevar $PSYCHIC_COST
	pause

	:FINDPLANETSCANNER
	getword CURRENTLINE $PLANET_SCANNER_COST 2
	striptext $PLANET_SCANNER_COST "Scanner="
	striptext $PLANET_SCANNER_COST ","
	striptext $PLANET_SCANNER_COST "$"
	savevar $PLANET_SCANNER_COST
	setvar $LSD_PSCAN $PLANET_SCANNER_COST
	savevar $LSD_PSCAN
	pause

	:FINDATOMIC
	getword CURRENTLINE $ATOMIC_COST 2
	striptext $ATOMIC_COST "Detonator="
	striptext $ATOMIC_COST ","
	striptext $ATOMIC_COST "$"
	savevar $ATOMIC_COST
	setvar $LSD_ATOMICCOST $ATOMIC_COST
	savevar $LSD_ATOMICCOST
	pause

	:REREGISTER
	killtrigger REREGISTER
	gosub :GETCOST
	setvar $LSD_REREGISTERCOST $LSD_COST
	savevar $LSD_REREGISTERCOST
	pause

	:FINDCORBO
	getword CURRENTLINE $CORBO_COST 1
	striptext $CORBO_COST "Corbomite="
	striptext $CORBO_COST ","
	striptext $CORBO_COST "$"
	savevar $CORBO_COST
	setvar $LSD_CORBOCOST $CORBO_COST
	savevar $LSD_CORBOCOST
	pause

	:FINDETHER
	getword CURRENTLINE $PROBE_COST 2
	striptext $PROBE_COST "Probe="
	striptext $PROBE_COST ","
	striptext $PROBE_COST "$"
	savevar $PROBE_COST
	setvar $LSD_EPROBE $PROBE_COST
	savevar $LSD_EPROBE
	pause

	:FINDPHOTON
	getword CURRENTLINE $PHOTON_COST 2
	striptext $PHOTON_COST "Missile="
	striptext $PHOTON_COST ","
	striptext $PHOTON_COST "$"
	savevar $PHOTON_COST
	setvar $LSD_PHOTONCOST $PHOTON_COST
	savevar $LSD_PHOTONCOST
	pause

	:FINDCLOAK
	getword CURRENTLINE $CLOAK_COST 2
	striptext $CLOAK_COST "Device="
	striptext $CLOAK_COST ","
	striptext $CLOAK_COST "$"
	savevar $CLOAK_COST
	setvar $LSD_CLOAKCOST $CLOAK_COST
	savevar $LSD_CLOAKCOST
	pause

	:FINDDISRUPTOR
	getword CURRENTLINE $DISRUPTOR_COST 2
	striptext $DISRUPTOR_COST "Disruptor="
	striptext $DISRUPTOR_COST ","
	striptext $DISRUPTOR_COST "$"
	savevar $DISRUPTOR_COST
	setvar $LSD_DISRUPTCOST $DISRUPTOR_COST
	savevar $LSD_DISRUPTCOST
	pause

	:FINDHOLOSCANNER
	getword CURRENTLINE $HOLO_COST 2
	striptext $HOLO_COST "Scanner="
	striptext $HOLO_COST ","
	striptext $HOLO_COST "$"
	savevar $HOLO_COST
	setvar $LSD_HOLOCOST $HOLO_COST
	savevar $LSD_HOLOCOST
	pause

	:FINDDENSITYSCAN
	getword CURRENTLINE $DENSITY_COST 2
	striptext $DENSITY_COST "Scanner="
	striptext $DENSITY_COST ","
	striptext $DENSITY_COST "$"
	savevar $DENSITY_COST
	setvar $LSD_DSCANCOST $DENSITY_COST
	savevar $LSD_DSCANCOST
	setvar $FILEHEADINGS "MBBS     COLO_REGEN     PTRADE     SF     RF     PORTMAX"
	setvar $FILEOUTPUT $MBBS&"     "&$COLONIST_REGEN&"     "&$PTRADESETTING&"     "&$STEAL_FACTOR&"     "&$ROB_FACTOR&"     "&$PORT_MAX
	delete $GAME_SETTINGS_FILE
	write $GAME_SETTINGS_FILE $FILEHEADINGS
	write $GAME_SETTINGS_FILE $FILEOUTPUT
	setvar $STEAL_FACTOR ((30 * $STEAL_FACTOR) / 100)
	savevar $STEAL_FACTOR
	setvar $ROB_FACTOR ((3 * 100) / $ROB_FACTOR)
	savevar $ROB_FACTOR
	send "t*n*"
	if (PASSWORD = "")
		send $PASSWORD
	else
		send PASSWORD
	end
	send "**  zaz*z*za9999*z*"
	if (($CURRENT_SECTOR > 11) and ($CURRENT_SECTOR <> STARDOCK))
		send "f1*cd"
	end
	gosub :QUIKSTATS
end
setvar $GAMESTATS TRUE
savevar $GAMESTATS
return

:FINDJUMPSECTOR
setvar $I 1
setvar $RED_ADJ 0
send "q t*t1* q*"
while (SECTOR.WARPSIN[$TARGET][$I] > 0)
	setvar $RED_ADJ SECTOR.WARPSIN[$TARGET][$I]
	if ($RED_ADJ > 10)
		send "m "&$RED_ADJ&"* y"
		settexttrigger TWARPBLIND :TWARPBLIND "Do you want to make this jump blind? "
		settexttrigger TWARPLOCKED :TWARPLOCKED "All Systems Ready, shall we engage? "
		settextlinetrigger TWARPVOIDED :TWARPVOIDED "Danger Warning Overridden"
		settextlinetrigger TWARPADJ :TWARPADJ "<Set NavPoint>"
		pause

		:TWARPADJ
		gosub :KILLTHETRIGGERS
		send " * "
		return

		:TWARPVOIDED
		gosub :KILLTHETRIGGERS
		send " N N "
		goto :TRYINGNEXTADJ

		:TWARPLOCKED
		gosub :KILLTHETRIGGERS
		goto :SECTORLOCKED

		:TWARPBLIND
		gosub :KILLTHETRIGGERS
		send " N "
	end

	:TRYINGNEXTADJ
	add $I 1
end

:NOADJSFOUND
setvar $RED_ADJ 0
return

:SECTORLOCKED
if ($TARGET = $STARDOCK)
	setvar $BACKDOOR $RED_ADJ
	savevar $BACKDOOR
end
return

:TWARPTO
setvar $TWARPSUCCESS FALSE
setvar $ORIGINAL 9999999
setvar $TARGET 0
if ($CURRENT_SECTOR = $WARPTO)
	setvar $MSG "Already in that sector!"
	goto :TWARPDONE
elseif (($WARPTO <= 0) or ($WARPTO > SECTORS))
	setvar $MSG "Destination sector is out of range!"
	goto :TWARPDONE
end
if ($TWARP_TYPE = "No")
	setvar $MSG "No T-warp drive on this ship!"
	goto :TWARPDONE
end
setvar $WEAREADJDOCK FALSE
if (($WARPTO = $STARDOCK) or ($WARPTO <= 10))
	setvar $TARGET $WARPTO
	setvar $A 1
	setvar $START_SECTOR $CURRENT_SECTOR
	while ($A <= SECTOR.WARPCOUNT[$START_SECTOR])
		setvar $ADJ_START SECTOR.WARPS[$START_SECTOR][$A]
		if ($ADJ_START = $TARGET)
			setvar $WEAREADJDOCK TRUE
		end
		add $A 1
	end
end
setvar $RED_ADJ 0
if (($ALIGNMENT < 1000) and ((($WEAREADJDOCK = FALSE) and (($WARPTO = $STARDOCK) or ($WARPTO <= 10)))))
	setvar $TARGET $WARPTO
	gosub :FINDJUMPSECTOR
	if ($RED_ADJ <> 0)
		setvar $ORIGINAL $WARPTO
		setvar $WARPTO $RED_ADJ
	else
		waitfor "Command [TL="
		setvar $MSG "Cannot Find Jump Sector Adjacent Sector "&$TARGET&"."
		goto :TWARPDONE
	end
end
if ($RED_ADJ <> 0)
	goto :TWARP_LOCK
end
if ($STARTINGLOCATION = "Citadel")
	send "q t*t1* q q * c u y q mz" $WARPTO "*"
elseif ($STARTINGLOCATION = "Planet")
	send "t*t1* q q * c u y q mz" $WARPTO "*"
else
	send "q q q n n 0 * c u y q mz" $WARPTO "*"
end
settexttrigger THERE :ADJ_WARP "You are already in that sector!"
settextlinetrigger ADJ_WARP :ADJ_WARP "Sector  : "&$WARPTO&" "
settexttrigger LOCKING :LOCKING "Do you want to engage the TransWarp drive?"
settexttrigger IGD :TWARPIGD "An Interdictor Generator in this sector holds you fast!"
settexttrigger NOTURNS :TWARPPHOTONED "Your ship was hit by a Photon and has been disabled"
settexttrigger NOROUTE :TWARPNOROUTE "Do you really want to warp there? (Y/N)"
pause

:ADJ_WARP
gosub :KILLTWARPTRIGGERS
send "z*"
goto :TWARP_ADJ

:LOCKING
gosub :KILLTWARPTRIGGERS
send "y"
settextlinetrigger TWARP_LOCK :TWARP_LOCK "TransWarp Locked"
settextlinetrigger NO_TWRP_LOCK :NO_TWARP_LOCK "No locating beam found"
settextlinetrigger TWARP_ADJ :TWARP_ADJ "<Set NavPoint>"
settextlinetrigger NO_FUEL :TWARPNOFUEL "You do not have enough Fuel Ore"
pause

:TWARPNOFUEL
gosub :KILLTWARPTRIGGERS
setvar $MSG "Not enough fuel for T-warp."
goto :TWARPDONE

:TWARP_ADJ
gosub :KILLTWARPTRIGGERS
send "z* "
setvar $MSG "That sector is next door, just plain warping."
setvar $TWARPSUCCESS TRUE
goto :TWARPDONE

:TWARPNOROUTE
gosub :KILLTWARPTRIGGERS
send "n* z* "
setvar $MSG "No route available to that sector!"
goto :TWARPDONE

:NO_TWARP_LOCK
gosub :KILLTWARPTRIGGERS
send "n* z* "
setvar $TARGET $WARPTO
gosub :REMOVEFIGFROMDATA
setvar $MSG "No fighters at T-warp point!"
goto :TWARPDONE

:TWARPIGD
gosub :KILLTWARPTRIGGERS
setvar $MSG "My ship is being held by Interdictor!"
goto :TWARPDONE

:TWARPPHOTONED
gosub :KILLTWARPTRIGGERS
setvar $MSG "I have been photoned and can not T-warp!"
goto :TWARPDONE

:TWARP_LOCK
gosub :KILLTWARPTRIGGERS
setvar $TARGET $WARPTO
gosub :ADDFIGTODATA
send "y* "
setvar $MSG "T-warp completed."
setvar $TWARPSUCCESS TRUE

:TWARPDONE
if (($TWARPSUCCESS = TRUE) and (($ORIGINAL = $STARDOCK) or ($ORIGINAL <= 10)))
	send "* m "&$ORIGINAL&"*  za9999* * "
end
return

:KILLTWARPTRIGGERS
killtrigger THERE
killtrigger ADJ_WARP
killtrigger LOCKING
killtrigger IGD
killtrigger NOTURNS
killtrigger NOROUTE
killtrigger TWARP_LOCK
killtrigger NO_TWRP_LOCK
killtrigger TWARP_ADJ
killtrigger NO_FUEL
return

:COMMAND_LIST
setvar $HELPLIST TRUE
if ($PARM1 = 0)
	gosub :QUIKSTATS
	setvar $MESSAGE "  --------------Mind ()ver Matter Bot Help Categories------------*"
	setvar $MESSAGE $MESSAGE&"                          Version: "&$MAJOR_VERSION&"_"&$MINOR_VERSION&"*"
	setvar $MESSAGE $MESSAGE&" *"
	setvar $MESSAGE $MESSAGE&"                [OFFENSE]|[DEFENSE]|[DATA]|[CASHING]*"
	setvar $MESSAGE $MESSAGE&"                     [RESOURCE]|[GRID]|[GENERAL]*"
	setvar $MESSAGE $MESSAGE&" *"
	setvar $MESSAGE $MESSAGE&"  ---------------------------------------------------------------*"
else
	getfilelist $COMMANDLIST "scripts\MomBot\Commands\"&$PARM1&"\*.cts"
	getfilelist $MODELIST "scripts\MomBot\Modes\"&$PARM1&"\*.cts"
	setvar $MAXSTRINGLENGTH 34
	setvar $PADDINGDASHES "                                 "
	uppercase $PARM1
	setvar $MESSAGE "  --Mind ()ver Matter Bot Commands--*"
	getlength "-="&$PARM1&"=-" $COMLENGTH
	setvar $SIDELENGTH (($MAXSTRINGLENGTH - $COMLENGTH) / 2)
	cuttext $PADDINGDASHES $LEFTPAD 1 $SIDELENGTH
	cuttext $PADDINGDASHES $RIGHTPAD 1 (($MAXSTRINGLENGTH - $COMLENGTH) - $SIDELENGTH)
	setvar $MESSAGE $MESSAGE&" |"&$LEFTPAD&"-="&$PARM1&"=-"&$RIGHTPAD&"|*"
	setvar $MESSAGE $MESSAGE&" |----------------------------------|*"
	setvar $I 1
	uppercase $CURRENTLIST
	while ($I <= $COMMANDLIST)
		setvar $TEMPCOMMAND $COMMANDLIST[$I]&"###"
		getword $CURRENTLIST $NEXT ($I + 1)
		getword $CURRENTLIST $NEXT2 ($I + 2)
		striptext $TEMPCOMMAND "scripts\MomBot\Commands\"&$PARM1&"\"
		striptext $TEMPCOMMAND ".cts###"
		uppercase $TEMPCOMMAND
		cuttext $TEMPCOMMAND&" " $HIDDEN 1 1
		if ($HIDDEN = "_")
			getlength $TEMPCOMMAND $TEMPLENGTH
			if (($SELF_COMMAND = TRUE) and ($TEMPLENGTH > 1))
				cuttext $TEMPCOMMAND $TEMPCOMMAND 2 9999
				setvar $CURRENTLIST $CURRENTLIST&" [<><>HIDDEN<><>]"&$TEMPCOMMAND&" "
			end
		else
			getwordpos $CURRENTLIST $POS " "&$TEMPCOMMAND&" "
			if ($POS <= 0)
				setvar $CURRENTLIST $CURRENTLIST&" "&$TEMPCOMMAND&" "
			end
		end
		add $I 1
	end
	setvar $MESSAGE $MESSAGE&" |           -=Commands=-           |*"
	setvar $COMMANDCOUNT 0
	setvar $BUFFERCOUNT 0
	gosub :BUFFERLIST
	if ($MODELIST > 0)
		setvar $MESSAGE $MESSAGE&" |            -=Modes=-             |*"
		setvar $CURRENTLIST " "
		setvar $I 1
		while ($I <= $MODELIST)
			setvar $TEMPCOMMAND $MODELIST[$I]&"###"
			striptext $TEMPCOMMAND "scripts\MomBot\Modes\"&$PARM1&"\"
			striptext $TEMPCOMMAND ".cts###"
			uppercase $TEMPCOMMAND
			cuttext $TEMPCOMMAND&" " $HIDDEN 1 1
			if ($HIDDEN = "_")
				getlength $TEMPCOMMAND $TEMPLENGTH
				if (($SELF_COMMAND = TRUE) and ($TEMPLENGTH > 1))
					cuttext $TEMPCOMMAND $TEMPCOMMAND 2 9999
					setvar $CURRENTLIST $CURRENTLIST&" [<><>HIDDEN<><>]"&$TEMPCOMMAND&" "
				end
			else
				setvar $CURRENTLIST $CURRENTLIST&" "&$TEMPCOMMAND&" "
			end
			add $I 1
		end
		gosub :BUFFERLIST
	end
	setvar $MESSAGE $MESSAGE&" |----------------------------------|*"
end
if ($SELF_COMMAND <> TRUE)
	setvar $SELF_COMMAND 2
end
gosub :SWITCHBOARD
goto :WAIT_FOR_COMMAND

:BUFFERLIST
setvar $I 1
getword $CURRENTLIST $TEST $I "[<><>NONE<><>]"
setvar $PADDINGDASHES "                                "
while ($TEST <> "[<><>NONE<><>]")
	setvar $TEMPCOMMAND $TEST
	setvar $TEMPCOMMANDHIDDEN FALSE
	setvar $NEXTHIDDEN FALSE
	setvar $NEXT2HIDDEN FALSE
	getword $CURRENTLIST $NEXT ($I + 1)
	getword $CURRENTLIST $NEXT2 ($I + 2)
	getwordpos $TEMPCOMMAND $POS "[<><>HIDDEN<><>]"
	if ($POS > 0)
		striptext $TEMPCOMMAND "[<><>HIDDEN<><>]"
		setvar $TEMPCOMMANDHIDDEN TRUE
		setvar $TEMPCOMMAND2 ANSI_14&$TEMPCOMMAND&ANSI_15
	else
		setvar $TEMPCOMMAND2 $TEMPCOMMAND
	end
	if ($NEXT <> 0)
		getwordpos $NEXT $POS "[<><>HIDDEN<><>]"
		striptext $NEXT "[<><>HIDDEN<><>]"
		if ($POS > 0)
			setvar $NEXTHIDDEN TRUE
			setvar $TEMPCOMMAND2 $TEMPCOMMAND2&"   "&ANSI_14&$NEXT&ANSI_15
		else
			setvar $TEMPCOMMAND2 $TEMPCOMMAND2&"   "&$NEXT
		end
		setvar $TEMPCOMMAND $TEMPCOMMAND&"   "&$NEXT
		add $I 1
	end
	if ($NEXT2 <> 0)
		getwordpos $NEXT2 $POS "[<><>HIDDEN<><>]"
		striptext $NEXT2 "[<><>HIDDEN<><>]"
		if ($POS > 0)
			setvar $NEXT2HIDDEN TRUE
			setvar $TEMPCOMMAND2 $TEMPCOMMAND2&"   "&ANSI_14&$NEXT2&ANSI_15
		else
			setvar $TEMPCOMMAND2 $TEMPCOMMAND2&"   "&$NEXT2
		end
		setvar $TEMPCOMMAND $TEMPCOMMAND&"   "&$NEXT2
		add $I 1
	end
	getlength $TEMPCOMMAND $COMLENGTH
	uppercase $TEMPCOMMAND
	setvar $SIDELENGTH (($MAXSTRINGLENGTH - $COMLENGTH) / 2)
	cuttext $PADDINGDASHES $LEFTPAD 1 $SIDELENGTH
	cuttext $PADDINGDASHES $RIGHTPAD 1 (($MAXSTRINGLENGTH - $COMLENGTH) - $SIDELENGTH)
	if ($SELF_COMMAND = TRUE)
		setvar $MESSAGE $MESSAGE&" |"&$LEFTPAD&$TEMPCOMMAND2&$RIGHTPAD&"|*"
	else
		setvar $MESSAGE $MESSAGE&" |"&$LEFTPAD&$TEMPCOMMAND&$RIGHTPAD&"|*"
	end
	add $COMMANDCOUNT 1
	add $I 1
	getword $CURRENTLIST $TEST $I "[<><>NONE<><>]"
end
return

:ECHO_HELP
echo "*"
echo ANSI_13 "  ----------------" ANSI_14 "Mind " ANSI_4 "()" ANSI_14 "ver Matter Bot Help Categories" ANSI_13 "---------------*"
echo ANSI_13 "                            Version: "&$MAJOR_VERSION&"."&$MINOR_VERSION&"*"
echo ANSI_13 "                  [OFFENSE]|[DEFENSE]|[DATA]|[CASHING]*"
echo ANSI_13 "                      [RESOURCE]|[GRID]|[GENERAL]    *"
echo ANSI_13 "  ------------------------------"&ANSI_14&"Hot Keys"&ANSI_13&"------------------------------*"
gosub :ECHOHOTKEYS
echo ANSI_13 "  ------------------------------"&ANSI_14&"Daemons"&ANSI_13&"-------------------------------*"
getfilelist $DAEMONLIST "scripts\MomBot\Daemons\*.cts"
if ($DAEMONLIST > 0)
	setvar $PADDINGDASHES "                                 "
	setvar $CURRENTLIST ""
	setvar $MAXSTRINGLENGTH 68
	setvar $I 1
	while ($I <= $DAEMONLIST)
		setvar $TEMPCOMMAND $DAEMONLIST[$I]&"###"
		striptext $TEMPCOMMAND "scripts\MomBot\Daemons\"&$PARM1&"\"
		striptext $TEMPCOMMAND ".cts###"
		setvar $CURRENTLIST $CURRENTLIST&" "&$TEMPCOMMAND&" "
		add $I 1
	end
	setvar $MESSAGE ""
	gosub :BUFFERLIST
	echo $MESSAGE
	echo ANSI_13 "  --------------------------------------------------------------------***"
end
goto :WAIT_FOR_COMMAND

:SS_HELP
setvar $HELPSTRING "'*"
setvar $HELPSTRING $HELPSTRING&"  -----------------Mind ()ver Matter Bot Help Categories--------------*"
setvar $HELPSTRING $HELPSTRING&"                              Version: "&$MAJOR_VERSION&"."&$MINOR_VERSION&"*"
setvar $HELPSTRING $HELPSTRING&"                   [OFFENSE]|[DEFENSE]|[DATA]|[CASHING]*"
setvar $HELPSTRING $HELPSTRING&"                        [RESOURCE]|[GRID]|[GENERAL]    *"
setvar $HELPSTRING $HELPSTRING&"  --------------------------------------------------------------------**"
send $HELPSTRING
goto :WAIT_FOR_COMMAND

:DISPLAYADJACENTANSI

:DISPLAYADJACENTGRIDANSI
setvar $I 1
isnumber $TEST CURRENTSECTOR
if ($TEST)
	while (SECTOR.WARPS[CURRENTSECTOR][$I] > 0)
		setvar $ADJ_SEC SECTOR.WARPS[CURRENTSECTOR][$I]
		setvar $CONTAINSSHIELDEDPLANET FALSE
		setvar $SHIELDEDPLANETS 0
		if ($ADJ_SEC >= 10000)
			setvar $ADJUST ""
		elseif ($ADJ_SEC >= 1000)
			setvar $ADJUST " "
		elseif ($ADJ_SEC >= 100)
			setvar $ADJUST "  "
		elseif ($ADJ_SEC >= 10)
			setvar $ADJUST "   "
		else
			setvar $ADJUST "    "
		end
		echo ANSI_13 "* (" ANSI_10 $I ANSI_13 ")" ANSI_15 " - " ANSI_13 "<" ANSI_14 SECTOR.WARPS[CURRENTSECTOR][$I] ANSI_13 ">" $ADJUST ANSI_15 " Warps: " ANSI_7 SECTOR.WARPCOUNT[$ADJ_SEC]
		getsectorparameter $ADJ_SEC "FIGSEC" $ISFIGGED
		getsectorparameter $ADJ_SEC "MSLSEC" $ISMSL
		if ($ISFIGGED = "")
			setvar $ISFIGGED FALSE
		end
		if ($ISMSL = "")
			setvar $ISMSL FALSE
		end
		setvar $ADJSECTOROWNER SECTOR.FIGS.OWNER[$ADJ_SEC]
		if ($ISFIGGED or ($ADJSECTOROWNER = "belong to your Corp") or ($ADJSECTOROWNER = "yours"))
			echo ANSI_15 " Owner: " ANSI_14 "   OURS   "
		else
			getword $ADJSECTOROWNER $ALIENCHECK 1
			if (($ADJ_SEC < 11) or ($ADJ_SEC = $STARDOCK))
				echo ANSI_15 " Owner: " ANSI_9 " FEDSPACE "
			elseif ($ADJ_SEC = $RYLOS)
				echo ANSI_15 " Owner: " ANSI_9 "  RYLOS   "
			elseif ($ADJ_SEC = $ALPHA_CENTAURI)
				echo ANSI_15 " Owner: " ANSI_9 "  ALPHA   "
			elseif ($ADJSECTOROWNER = "Rogue Mercenaries")
				echo ANSI_15 " Owner: " ANSI_7 "  ROGUE   "
			elseif ($ALIENCHECK = "the")
				echo ANSI_15 " Owner: " ANSI_2 "  ALIENS  "
			elseif ($ALIENCHECK = "The")
				echo ANSI_15 " Owner: " ANSI_2 "  ALIENS  "
			elseif (($ADJSECTOROWNER <> "") and ($ADJSECTOROWNER <> "Unknown"))
				setvar $HEADS TRUE
				getword $ADJSECTOROWNER $TEMP 3
				striptext $TEMP ","
				uppercase $TEMP
				getlength $TEMP $TEMPLENGTH
				if ($TEMPLENGTH >= 10)
					cuttext $TEMP $TEMP 1 10
				else
					while ((10 - $TEMPLENGTH) > 0)
						if ($HEADS)
							setvar $TEMP $TEMP&" "
							setvar $HEADS FALSE
						else
							setvar $TEMP " "&$TEMP
							setvar $HEADS TRUE
						end
						getlength $TEMP $TEMPLENGTH
					end
				end
				echo ANSI_15 " Owner: " ANSI_12 $TEMP
			else
				echo ANSI_15 " Owner: " ANSI_13 "   NONE   "
			end
		end
		if (SECTOR.ANOMOLY[$ADJ_SEC])
			echo ANSI_15 " Anom: " ANSI_11 "Yes" ANSI_15
		else
			echo ANSI_15 " Anom: " ANSI_7 " No" ANSI_15
		end
		echo ANSI_15 "  Dens: " ANSI_14
		if (SECTOR.DENSITY[$ADJ_SEC] = "-1")
			echo "???        "
		else
			setvar $DENS SECTOR.DENSITY[$ADJ_SEC]
			getlength SECTOR.DENSITY[$ADJ_SEC] $DENSLENGTH
			if ($DENSLENGTH >= 9)
				echo "HIGH      "
			else
				setvar $D $DENSLENGTH
				while ($D <= 10)
					setvar $DENS $DENS&" "
					add $D 1
				end
				echo $DENS
			end
		end
		if ($ISMSL = TRUE)
			echo ANSI_15 "[" ANSI_14 "MSL" ANSI_15 "]" ANSI_7
		end
		setvar $P 1
		if (SECTOR.PLANETCOUNT[$ADJ_SEC] > 0)
			echo ANSI_15 "*        Planet(s): " ANSI_7
		end
		while ($P <= SECTOR.PLANETCOUNT[$ADJ_SEC])
			echo "*             " ANSI_14 SECTOR.PLANETS[$ADJ_SEC][$P]
			add $P 1
		end
		setvar $P 1
		if (SECTOR.TRADERCOUNT[$ADJ_SEC] > 0)
			echo ANSI_15 "*        Trader(s): " ANSI_7
		end
		while ($P <= SECTOR.TRADERCOUNT[$ADJ_SEC])
			echo "*             " ANSI_14 SECTOR.TRADERS[$ADJ_SEC][$P]
			add $P 1
		end
		add $I 1
	end
	setvar $GRIDWARPCOUNT ($I - 1)
else
	echo ANSI_15 " ERROR WITH CURRENTSECTOR  " ANSI_7
end
echo "**" CURRENTANSILINE
return

:MOVEINTOSECTOR
setvar $RESULT ""
setvar $DROPFIGS TRUE
if ($SHIP_MAX_ATTACK <= 0)
	setvar $ATTACK 9999
else
	setvar $ATTACK $SHIP_MAX_ATTACK&9999
end
setvar $RESULT $RESULT&"m "&$MOVEINTOSECTOR&"* y * "
if (($MOVEINTOSECTOR > 10) and ($MOVEINTOSECTOR <> $STARDOCK))
	setvar $RESULT $RESULT&"za"&$ATTACK&"* * "
end
if (($DROPFIGS = TRUE) and (($MOVEINTOSECTOR > 10) and ($MOVEINTOSECTOR <> $STARDOCK)))
	setvar $RESULT $RESULT&"f 1 * c d "
	setvar $TARGET $MOVEINTOSECTOR
	gosub :ADDFIGTODATA
end
send $RESULT&"*"
settexttrigger MOVEIN_THERE :MOVEIN_THERE "["&$MOVEINTOSECTOR&"]"
settexttrigger MOVEIN_NOPE :MOVEIN_NOPE "(A,D,I,R,?):? D"
pause

:MOVEIN_NOPE
killtrigger MOVEIN_THERE
send "R"

:MOVEIN_THERE
killtrigger MOVEIN_NOPE
return

:HOLO_KILL

:HKILL
gosub :KILLTHETRIGGERS
gosub :CURRENT_PROMPT
setvar $VALIDPROMPTS "Citadel Command"
gosub :CHECKSTARTINGPROMPT
if ($STARTINGLOCATION = "Citadel")
	send " q dm *** c  "
	waitfor "Planet command (?=help) [D]"
	waitfor "Planet #"
	getword CURRENTLINE $PLANETNUM 2
	striptext $PLANETNUM "#"
	waitfor "Citadel command (?=help)"
end
send " c ;q"
waitfor "Max Fighters:"
setvar $LINE CURRENTLINE
replacetext $LINE ":" " "
getword $LINE $MAX_FIGS 7
striptext $MAX_FIGS ","
waitfor "Max Figs Per Attack:"
setvar $LINE CURRENTLINE
replacetext $LINE ":" " "
getword $LINE $MAX_FIG_WAVE 5
striptext $MAX_FIG_WAVE ","
if ($MAX_FIG_WAVE = $MAX_FIGS)
	setvar $MAX_FIG_WAVE ($MAX_FIG_WAVE - 100)
end
setvar $WAVES_TO_SEND ($MAX_FIGS / $MAX_FIG_WAVE)

:HOLO_KILL_KILL_CHECK
settextlinetrigger NOSCAN1 :HOLO_KILL_NOSCANNER "Handle which mine type, 1 Armid or 2 Limpet"
settextlinetrigger NOSCAN2 :HOLO_KILL_NOSCANNER "You don't have a long range scanner."
settextlinetrigger SCANNED :HOLO_KILL_SCANDONE "Select (H)olo Scan or (D)ensity Scan or (Q)uit? [D] H"
if ($CURRENT_PROMPT = "Citadel")
	send " qqqz* sh*  l "&$PLANETNUM&" * j c * "
else
	send " sh*  "
end
pause

:HOLO_KILL_NOSCANNER
gosub :KILLTHETRIGGERS
setvar $MESSAGE "You don't have a HoloScanner!*"
gosub :SWITCHBOARD
send " *  "
goto :WAIT_FOR_COMMAND

:HOLO_KILL_SCANDONE
gosub :KILLTHETRIGGERS
waitfor "Warps to Sector(s) :"

:HOLO_KILL_GET_PROMPT
waitfor "Command [TL="
gettext CURRENTLINE $CURRENT_SECTOR "]:[" "] (?="

:HOLO_KILL_GET_CURRENT_SECTOR
isnumber $RESULT $CURRENT_SECTOR
if ($RESULT < 1)
	send "/"
	setvar $LINE CURRENTLINE
	replacetext $LINE #179 " "
	getword $LINE $CURRENT_SECTOR 2
	goto :HOLO_KILL_GET_CURRENT_SECTOR
end
if (($CURRENT_SECTOR < 1) or ($CURRENT_SECTOR > SECTORS))
	send "/"
	setvar $LINE CURRENTLINE
	replacetext $LINE #179 " "
	getword $LINE $CURRENT_SECTOR 2
	goto :HOLO_KILL_GET_CURRENT_SECTOR
end
setvar $KILLSECTOR 0
setvar $IDX 1
while ($IDX <= SECTOR.WARPCOUNT[$CURRENT_SECTOR])
	setvar $TEST_SECTOR SECTOR.WARPS[$CURRENT_SECTOR][$IDX]
	setvar $SAFEPLANETS TRUE
	setvar $CONTAINSSHIELDEDPLANET FALSE
	if (SECTOR.PLANETCOUNT[$TEST_SECTOR] > 0)
		setvar $P 1
		while ($P <= SECTOR.PLANETCOUNT[$TEST_SECTOR])
			getword SECTOR.PLANETS[$TEST_SECTOR][$P] $TEST 1
			if ($TEST = "<<<<")
				setvar $CONTAINSSHIELDEDPLANET TRUE
			end
			add $P 1
		end
		if ($SURROUNDAVOIDALLPLANETS)
			setvar $SAFEPLANETS FALSE
		elseif ($CONTAINSSHIELDEDPLANET and $SURROUNDAVOIDSHIELDEDONLY)
			setvar $SAFEPLANETS FALSE
		end
	end
	if (($TEST_SECTOR <> $STARDOCK) and (($TEST_SECTOR > 10) and ((SECTOR.TRADERCOUNT[$TEST_SECTOR] > 0) and ($SAFEPLANETS = TRUE))))
		setvar $KILLSECTOR $TEST_SECTOR
		goto :HOLO_KILL_KILLEM
	end
	add $IDX 1
end

:HOLO_KILL_KILLEM
if (($KILLSECTOR > 10) and ($KILLSECTOR <> $STARDOCK))
	send "'{" $BOT_NAME "} - Dny HoloKill - Attacking sector "&$TEST_SECTOR&".*"
	setvar $NO_STR ""
	setvar $NO_CNT SECTOR.SHIPCOUNT[$KILLSECTOR]
	setvar $NO_IDX 1
	while ($NO_IDX <= $NO_CNT)
		setvar $NO_STR $NO_STR&"n"
		add $NO_IDX 1
	end
	send " c v 0 * y n "&$TEST_SECTOR&" * q "
	if ($STARTINGLOCATION = "Citadel")
		send " qqqz* "
	end
	send " m z "&$TEST_SECTOR&" *  *  z  a  99999  *  z  a  99999  *  R  *  f  z  1  *  z  c  d  *   "
	setvar $KILL_IDX 1
	while ($KILL_IDX <= $WAVES_TO_SEND)
		send " a "&$NO_CNT&" y n y q z "&$MAX_FIG_WAVE&" * "
		add $KILL_IDX 1
	end
	send " DZ N  R  *  <  N  N  *  Z  A  99999  *  "
	if ($STARTINGLOCATION = "Citadel")
		send " l "&$PLANETNUM&" * n n * j m * * * j c  *  "
	end
else
	if ($STARTINGLOCATION = "Citadel")
		send " s* "
		waitfor "<Scan Sector>"
		waitfor "Citadel command (?=help)"
		setvar $MESSAGE "No Enemies found adjacent!*"
		gosub :SWITCHBOARD
		send " *  "
	else
		send " dz * "
		waitfor "<Re-Display>"
		waitfor "Command [TL="
		setvar $MESSAGE "No Enemies found adjacent!*"
		gosub :SWITCHBOARD
		send " *  "
	end
end
goto :WAIT_FOR_COMMAND

:SWITCHBOARD
setvar $MSG_HEADER_ECHO ANSI_9&"{"&ANSI_14&$BOT_NAME&ANSI_9&"} "&ANSI_15
setvar $MSG_HEADER_SS_1 "'{"&$BOT_NAME&"} - "
setvar $MSG_HEADER_SS_2 "'*{"&$BOT_NAME&"} - *"
if ($MESSAGE <> "")
	gosub :KILLTHETRIGGERS
	if ($SELF_COMMAND)
		setvar $LENGTH 0
	else
		getlength $BOT_NAME $LENGTH
	end
	setvar $I 1
	setvar $SPACING ""
	if ($SELF_COMMAND <> 0)
		if ($SELF_COMMAND > 1)
			striptext $MESSAGE ANSI_1
			striptext $MESSAGE ANSI_2
			striptext $MESSAGE ANSI_3
			striptext $MESSAGE ANSI_4
			striptext $MESSAGE ANSI_5
			striptext $MESSAGE ANSI_6
			striptext $MESSAGE ANSI_7
			striptext $MESSAGE ANSI_8
			striptext $MESSAGE ANSI_9
			striptext $MESSAGE ANSI_10
			striptext $MESSAGE ANSI_11
			striptext $MESSAGE ANSI_12
			striptext $MESSAGE ANSI_13
			striptext $MESSAGE ANSI_14
			striptext $MESSAGE ANSI_15
			if ($HELPLIST <> TRUE)
				striptext $MESSAGE "    "
			end
		end
		while ($I <= $LENGTH)
			setvar $SPACING $SPACING&" "
			add $I 1
		end
		setvar $NEW_MESSAGE ""
		setvar $MESSAGE_LINE ""
		replacetext $MESSAGE "**" "{END_OF_LINE}"
		replacetext $MESSAGE "*" "{END_OF_LINE}"
		gettext "{START_OF_MESSAGE}"&$MESSAGE $MESSAGE_LINE "{START_OF_MESSAGE}" "{END_OF_LINE}"
		while ($MESSAGE_LINE <> "")
			setvar $NEW_MESSAGE $NEW_MESSAGE&$SPACING&$MESSAGE_LINE&"*"
			getlength "{START_OF_MESSAGE}"&$MESSAGE_LINE&"{END_OF_LINE}" $CUTLENGTH
			cuttext "{START_OF_MESSAGE}"&$MESSAGE&"     " $MESSAGE ($CUTLENGTH + 1) 99999
			gettext "{START_OF_MESSAGE}"&$MESSAGE $MESSAGE_LINE "{START_OF_MESSAGE}" "{END_OF_LINE}"
		end
	else
		setvar $NEW_MESSAGE $MESSAGE
	end
	if ($SELF_COMMAND = 1)
		echo "*"&$MSG_HEADER_ECHO&$NEW_MESSAGE
		send #145
	elseif ($SELF_COMMAND = 0)
		send $MSG_HEADER_SS_1&$NEW_MESSAGE
	elseif ($SELF_COMMAND > 1)
		send $MSG_HEADER_SS_2&$NEW_MESSAGE&"*"
	end
	setvar $MESSAGE ""
end
setvar $HELPLIST FALSE
return

:DOCK_SHOPPER
setvar $ISDOCKSHOPPER TRUE
setvar $LSD__ATOMICS ""
setvar $LSD__BEACONS ""
setvar $LSD__CORBO ""
setvar $LSD__CLOAK ""
setvar $LSD__PROBE ""
setvar $LSD__PSCAN ""
setvar $LSD__LIMPS ""
setvar $LSD__MINES ""
setvar $LSD__PHOTON ""
setvar $LSD__LRSCAN ""
setvar $LSD__DISRUPT ""
setvar $LSD__GENTORP ""
setvar $LSD__T2TWARP ""
setvar $LSD__HOLDS ""
setvar $LSD__FIGS ""
setvar $LSD__SHIELDS ""
setvar $LSD__TRICKSTER ""
setvar $LSD_NUMBEROFSHIP ""
setvar $LSD__TOTAL 0
setvar $LSD_TOW 0
setvar $LSD_ORDER ""
gosub :QUIKSTATS
setvar $VALIDPROMPTS "Command Citadel"
gosub :CHECKSTARTINGPROMPT
if ($STARTINGLOCATION = "Citadel")
	send " Q DC  "
	waitfor "Planet #"
	getword CURRENTLINE $PLANET 2
	striptext $PLANET "#"
	isnumber $LSD_TST $PLANET
	if ($LSD_TST = 0)
		setvar $PLANET 0
	end
end
gosub :LOADSHIPDATA
gosub :GETCLASS0COSTS
gosub :CHECKCOSTS

:START

:TOPOFMENU
echo #27&"[2J"

:TOPOFMENU_NOCLEAR
gosub :SETMENUECHOS
echo "***"
echo "     "&ANSI_15&#196&#196&ANSI_7&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_7&#196&ANSI_15&#196&#196
echo ANSI_14&"*        LoneStar's StarDock Shopper"
echo ANSI_9&"*         Mind ()ver Matter Edition"
echo ANSI_15&"*          Emporium Daily Specials"
echo ANSI_14&"*                Version "&$LSD_CURENT_VERSION&"*"
echo "     "&ANSI_15&#196&#196&ANSI_7&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_7&#196&ANSI_15&#196&#196
echo "*"
setvar $LSD_PADTHISCOST $LSD_ATOMICCOST
gosub :PADITEMCOSTS
echo ANSI_5&"*    <"&ANSI_2&"A"&ANSI_5&">"&ANSI_9&" Atomic Detonators      "&$LSD_PADTHISCOST&ANSI_14&": "&$LSD_ECHO_ATOMICS
setvar $LSD_PADTHISCOST $LSD_BEACON
gosub :PADITEMCOSTS
echo ANSI_5&"*    <"&ANSI_2&"B"&ANSI_5&">"&ANSI_9&" Marker Beacons         "&$LSD_PADTHISCOST&ANSI_14&": "&$LSD_ECHO_BEACONS
setvar $LSD_PADTHISCOST $LSD_CORBOCOST
gosub :PADITEMCOSTS
echo ANSI_5&"*    <"&ANSI_2&"C"&ANSI_5&">"&ANSI_9&" Corbomite Devices      "&$LSD_PADTHISCOST&ANSI_14&": "&$LSD_ECHO_CORBO
setvar $LSD_PADTHISCOST $LSD_CLOAKCOST
gosub :PADITEMCOSTS
echo ANSI_5&"*    <"&ANSI_2&"D"&ANSI_5&">"&ANSI_9&" Cloaking Devices       "&$LSD_PADTHISCOST&ANSI_14&": "&$LSD_ECHO_CLOAK
setvar $LSD_PADTHISCOST $LSD_EPROBE
gosub :PADITEMCOSTS
echo ANSI_5&"*    <"&ANSI_2&"E"&ANSI_5&">"&ANSI_9&" SubSpace Ether Probes  "&$LSD_PADTHISCOST&ANSI_14&": "&$LSD_ECHO_PROBE
setvar $LSD_PADTHISCOST $LSD_PSCAN
gosub :PADITEMCOSTS
echo ANSI_5&"*    <"&ANSI_2&"F"&ANSI_5&">"&ANSI_9&" Planet Scanners        "&$LSD_PADTHISCOST&ANSI_14&": "&$LSD_ECHO_PSCAN
setvar $LSD_PADTHISCOST $LSD_LIMPCOST
gosub :PADITEMCOSTS
echo ANSI_5&"*    <"&ANSI_2&"L"&ANSI_5&">"&ANSI_9&" Limpet Tracking Mines  "&$LSD_PADTHISCOST&ANSI_14&": "&$LSD_ECHO_LIMPS
setvar $LSD_PADTHISCOST $LSD_ARMIDCOST
gosub :PADITEMCOSTS
echo ANSI_5&"*    <"&ANSI_2&"M"&ANSI_5&">"&ANSI_9&" Space Mines            "&$LSD_PADTHISCOST&ANSI_14&": "&$LSD_ECHO_MINES
setvar $LSD_PADTHISCOST $LSD_PHOTONCOST
gosub :PADITEMCOSTS
echo ANSI_5&"*    <"&ANSI_2&"P"&ANSI_5&">"&ANSI_9&" Photon Missiles        "&$LSD_PADTHISCOST&ANSI_14&": "&$LSD_ECHO_PHOTON
setvar $LSD_PADTHISCOST $LSD_HOLOCOST
gosub :PADITEMCOSTS
echo ANSI_5&"*    <"&ANSI_2&"R"&ANSI_5&">"&ANSI_9&" Long Range Scanners    "&$LSD_PADTHISCOST&ANSI_14&": "&$LSD_ECHO_LRSCAN
setvar $LSD_PADTHISCOST $LSD_DISRUPTCOST
gosub :PADITEMCOSTS
echo ANSI_5&"*    <"&ANSI_2&"S"&ANSI_5&">"&ANSI_9&" Mine Disruptors        "&$LSD_PADTHISCOST&ANSI_14&": "&$LSD_ECHO_DISRUPT
setvar $LSD_PADTHISCOST $LSD_GENCOST
gosub :PADITEMCOSTS
echo ANSI_5&"*    <"&ANSI_2&"T"&ANSI_5&">"&ANSI_9&" Genesis Torpedoes      "&$LSD_PADTHISCOST&ANSI_14&": "&$LSD_ECHO_GENTORP
setvar $LSD_PADTHISCOST $LSD_TWARPIICOST
gosub :PADITEMCOSTS
echo ANSI_5&"*    <"&ANSI_2&"W"&ANSI_5&">"&ANSI_9&" T2 TransWarp Drives    "&$LSD_PADTHISCOST&ANSI_14&": "&$LSD_ECHO_T2TWARP
setvar $LSD_PADTHISCOST $LSD_HOLDCOST
gosub :PADITEMCOSTS
echo ANSI_5&"*    <"&ANSI_2&1&ANSI_5&">"&ANSI_9&" Holds                  "&$LSD_PADTHISCOST&ANSI_14&": "&$LSD_ECHO_HOLDS
setvar $LSD_PADTHISCOST $LSD_FIGHTERCOST
gosub :PADITEMCOSTS
echo ANSI_5&"*    <"&ANSI_2&2&ANSI_5&">"&ANSI_9&" Figs                   "&$LSD_PADTHISCOST&ANSI_14&": "&$LSD_ECHO_FIGS
setvar $LSD_PADTHISCOST $LSD_SHIELD
gosub :PADITEMCOSTS
echo ANSI_5&"*    <"&ANSI_2&3&ANSI_5&">"&ANSI_9&" Shields                "&$LSD_PADTHISCOST&ANSI_14&": "&$LSD_ECHO_SHIELDS
if ($LSD__TOTAL <> 0)
	setvar $LSD_CASHAMOUNT $LSD__TOTAL
	gosub :COMMASIZE
	echo "*                                 "&ANSI_15&" TOTAL ("&ANSI_7&"$"&$LSD_CASHAMOUNT&ANSI_15&")"
	setvar $LSD__TOTAL 0
end
echo "*    " #27 "[1m" ANSI_4 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196
if ($LSD_SHIPDATA_VALID)
	echo ANSI_5&"*    <"&ANSI_8&"G"&ANSI_5&">"&ANSI_5&" Buy Ship(s): "&ANSI_8&$LSD_ECHO_TRICKSTER
else
	echo ANSI_5&"*    <"&ANSI_8&"G"&ANSI_5&">"&ANSI_5&" Buy Ship(s): "&ANSI_8&"Must Run StandAlone Version"
	setvar $LSD__TRICKSTER ""
end
if ($LSD__TRICKSTER = "")
	echo ANSI_5&"*    <"&ANSI_8&"Y"&ANSI_5&">"&ANSI_5&" Tow & Outfit Another Ship   "&ANSI_8
	if ($LSD_TOW > 0)
		echo ANSI_15&"#"&$LSD_TOW
	end
else
	setvar $LSD_TOW 0
end
echo ANSI_5&"*    <"&ANSI_8&"Z"&ANSI_5&">"&ANSI_5&" Max Out Ship On Everything!"
echo ANSI_5&"*    <"&ANSI_15&"V"&ANSI_5&">"&ANSI_5&" Name Of Bot To Command "&ANSI_14&": "
if (($LSD_BOTTING = "") or ($LSD_BOTTING = 0))
	setvar $LSD_BOTTING $BOT_NAME
end
echo ANSI_15&$LSD_BOTTING
echo "*        " #27 "[1m" ANSI_4 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196
echo "*        "&ANSI_14&"X"&ANSI_15&" - Execute    "&ANSI_14&"Q"&ANSI_15&" - Quit**"
getconsoleinput $LSD_SELECTION SINGLEKEY
uppercase $LSD_SELECTION
setvar $YES_NO FALSE
setvar $ITEM_MAX 1000
if ($LSD_SELECTION = "Q")
	echo "**"&ANSI_12 "  Script Halted"&ANSI_15&"**"
	goto :WAIT_FOR_COMMAND
elseif ($LSD_SELECTION = "A")
	setvar $ITEM_NAME "Atomics"
	setvar $ITEM_MAX 100
	gosub :GETITEMINPUT
	setvar $LSD__ATOMICS $LSD_SELECTION
elseif ($LSD_SELECTION = "B")
	setvar $ITEM_NAME "Marker Beacons"
	setvar $ITEM_MAX 100
	gosub :GETITEMINPUT
	setvar $LSD__BEACONS $LSD_SELECTION
elseif ($LSD_SELECTION = "C")
	setvar $ITEM_NAME "Coromite Devices"
	setvar $ITEM_MAX 100000
	gosub :GETITEMINPUT
	setvar $LSD__CORBO $LSD_SELECTION
elseif ($LSD_SELECTION = "D")
	setvar $ITEM_NAME "Cloaking Devices"
	gosub :GETITEMINPUT
	setvar $LSD__CLOAK $LSD_SELECTION
elseif ($LSD_SELECTION = "E")
	setvar $ITEM_NAME "SubSpace Ether Probe Devices"
	gosub :GETITEMINPUT
	setvar $LSD__PROBE $LSD_SELECTION
elseif ($LSD_SELECTION = "F")
	setvar $ITEM_NAME "Install Planet Scanner (Y/N)?"
	setvar $YES_NO TRUE
	gosub :GETITEMINPUT
	setvar $LSD__PSCAN $LSD_SELECTION
elseif ($LSD_SELECTION = "L")
	setvar $ITEM_NAME "Limpet Tracking Devices"
	gosub :GETITEMINPUT
	setvar $LSD__LIMPS $LSD_SELECTION
elseif ($LSD_SELECTION = "M")
	setvar $ITEM_NAME "Armid Mines To Buy"
	gosub :GETITEMINPUT
	setvar $LSD__MINES $LSD_SELECTION
elseif ($LSD_SELECTION = "P")
	setvar $ITEM_NAME "Photon Devices To Buy"
	gosub :GETITEMINPUT
	setvar $LSD__PHOTON $LSD_SELECTION
elseif ($LSD_SELECTION = "R")
	setvar $ITEM_NAME "Holo Scanner (Y/N)?"
	setvar $YES_NO TRUE
	gosub :GETITEMINPUT
	setvar $LSD__LRSCAN $LSD_SELECTION
elseif ($LSD_SELECTION = "S")
	setvar $ITEM_NAME "Mine Disruptors"
	gosub :GETITEMINPUT
	setvar $LSD__DISRUPT $LSD_SELECTION
elseif ($LSD_SELECTION = "T")
	setvar $ITEM_NAME "Genesis Torpedoes"
	gosub :GETITEMINPUT
	setvar $LSD__GENTORP $LSD_SELECTION
elseif ($LSD_SELECTION = "W")
	setvar $ITEM_NAME "Install Trans Warp 2 Drive (Y/N)?"
	setvar $YES_NO TRUE
	gosub :GETITEMINPUT
	setvar $LSD__T2TWARP $LSD_SELECTION
elseif ($LSD_SELECTION = "Y")
	if ($TWARP_TYPE = 2)
		getinput $LSD_SELECTION ANSI_15&#27&"[1A"&#27&"[K"&ANSI_14&"*Tow and Outfit a Ship (0 to Cancel)?"
		isnumber $LSD_TST $LSD_SELECTION
		if ($LSD_TST <> 0)
			if (($LSD_SELECTION < 0) or ($LSD_SELECTION > 250))
				setvar $LSD_TOW 0
			else
				setvar $LSD_TOW $LSD_SELECTION
			end
		else
			setvar $LSD_TOW 0
		end
	end
elseif ($LSD_SELECTION = "Z")
	setvar $LSD__PHOTON "Max"

	:BUYPHOTONENTHOUGHTHERESHAZ2
	setvar $LSD__TOTAL 0
	setvar $LSD__ATOMICS "Max"
	setvar $LSD__BEACONS "Max"
	setvar $LSD__CORBO "Max"
	setvar $LSD__CLOAK "Max"
	setvar $LSD__PROBE "Max"
	setvar $LSD__PSCAN "Yes"
	setvar $LSD__LIMPS "Max"
	setvar $LSD__MINES "Max"
	setvar $LSD__LRSCAN "Yes"
	setvar $LSD__DISRUPT "Max"
	setvar $LSD__GENTORP "Max"
	setvar $LSD__T2TWARP "Yes"
	setvar $LSD__HOLDS "Max"
	setvar $LSD__FIGS "Max"
	setvar $LSD__SHIELDS "Max"
elseif ($LSD_SELECTION = "V")
	getinput $LSD_BOTTING "  "&ANSI_5&"Enter the Bot Name To Issue LSD Command Too? "
	if ($LSD_BOTTING = $LSD__PAD)
		setvar $LSD_BOTTING $BOT_NAME
	end
elseif ($LSD_SELECTION = 1)
	setvar $ITEM_NAME "Cargo Holds"
	setvar $ITEM_MAX 255
	gosub :GETITEMINPUT
	setvar $LSD__HOLDS $LSD_SELECTION
elseif ($LSD_SELECTION = 2)
	setvar $ITEM_NAME "Fighters"
	setvar $ITEM_MAX 400000
	gosub :GETITEMINPUT
	setvar $LSD__FIGS $LSD_SELECTION
elseif ($LSD_SELECTION = 3)
	setvar $ITEM_NAME "Shields"
	setvar $ITEM_MAX 16000
	gosub :GETITEMINPUT
	setvar $LSD__SHIELDS $LSD_SELECTION
elseif (($LSD_SELECTION = "G") and $LSD_SHIPDATA_VALID)
	gosub :DISPLAYMENU
elseif ($LSD_SELECTION = "X")
	if (($LSD__ATOMICS = "") and (($LSD__BEACONS = "") and (($LSD__CORBO = "") and (($LSD__CLOAK = "") and (($LSD__PROBE = "") and (($LSD__PSCAN = "") and (($LSD__LIMPS = "") and (($LSD__MINES = "") and (($LSD__PHOTON = "") and (($LSD__LRSCAN = "") and (($LSD__DISRUPT = "") and (($LSD__GENTORP = "") and (($LSD__T2TWARP = "") and (($LSD__BUFFERS = "") and (($LSD__HOLDS = "") and (($LSD__FIGS = "") and ($LSD__SHIELDS = "")))))))))))))))))
		if ($LSD__TRICKSTER = "")
			echo "**"&ANSI_14&$LSD_TAGLINEB&ANSI_15&" - Nothing Was Selected From The Menu**"
			goto :TOPOFMENU_NOCLEAR
		end
	end
	if (($LSD_BOTTING = "") or ($LSD_BOTTING = $LSD__PAD))
		echo "****"&ANSI_14&$LSD_TAGLINEB&ANSI_15&" - Please specify name of Bot to address!"
		goto :TOPOFMENU_NOCLEAR
	end
	echo "**" ANSI_15
	setvar $ITEM_TYPE $LSD__ATOMICS
	gosub :PREPAREORDER
	setvar $ITEM_TYPE $LSD__BEACONS
	gosub :PREPAREORDER
	setvar $ITEM_TYPE $LSD__CORBO
	gosub :PREPAREORDER
	setvar $ITEM_TYPE $LSD__CLOAK
	gosub :PREPAREORDER
	setvar $ITEM_TYPE $LSD__PROBE
	gosub :PREPAREORDER
	setvar $ITEM_TYPE $LSD__PSCAN
	setvar $YES_NO TRUE
	gosub :PREPAREORDER
	setvar $ITEM_TYPE $LSD__LIMPS
	gosub :PREPAREORDER
	setvar $ITEM_TYPE $LSD__MINES
	gosub :PREPAREORDER
	setvar $ITEM_TYPE $LSD__PHOTON
	gosub :PREPAREORDER
	setvar $ITEM_TYPE $LSD__LRSCAN
	setvar $YES_NO TRUE
	gosub :PREPAREORDER
	setvar $ITEM_TYPE $LSD__DISRUPT
	gosub :PREPAREORDER
	setvar $ITEM_TYPE $LSD__GENTORP
	gosub :PREPAREORDER
	setvar $ITEM_TYPE $LSD__T2TWARP
	setvar $YES_NO TRUE
	gosub :PREPAREORDER
	setvar $ITEM_TYPE $LSD__HOLDS
	gosub :PREPAREORDER
	setvar $ITEM_TYPE $LSD__FIGS
	gosub :PREPAREORDER
	setvar $ITEM_TYPE $LSD__SHIELDS
	gosub :PREPAREORDER
	if ($LSD_TOW <> "")
		setvar $LSD_ORDER $LSD_ORDER&$LSD_TOW
	else
		setvar $LSD_ORDER $LSD_ORDER&0
	end
	setvar $LSD_ORDER $LSD_ORDER&$LSD__PAD
	if ($LSD__TRICKSTER <> "")
		getwordpos $LSD__TRICKSTER $LSD_POS "^^"
		cuttext $LSD__TRICKSTER $LSD__TRICKSTER 1 ($LSD_POS - 1)
		striptext $LSD__TRICKSTER " "
		striptext $LSD__TRICKSTER "^"
	end
	if ($LSD__TRICKSTER <> "")
		setvar $LSD_ORDER $LSD_ORDER&$LSD__TRICKSTER
	else
		setvar $LSD_ORDER $LSD_ORDER&0
	end
	setvar $LSD_ORDER $LSD_ORDER&$LSD__PAD
	if ($LSD_NUMBEROFSHIP <> "")
		setvar $LSD_ORDER $LSD_ORDER&$LSD_NUMBEROFSHIP
	else
		setvar $LSD_ORDER $LSD_ORDER&0
	end
	setvar $LSD_ORDER $LSD_ORDER&$LSD__PAD
	if ($LSD_CUSTOMSHIPNAME <> "")
		setvar $LSD_ORDER $LSD_ORDER&$LSD_CUSTOMSHIPNAME
	else
		setvar $LSD_ORDER $LSD_ORDER&$LSD_SHIPS_NAMES
	end
	if ($LSD_BOTTING = $BOT_NAME)
		setvar $LSD_ORDER $LSD_ORDER&"              "
		setvar $USER_COMMAND_LINE "lsd "&$LSD_ORDER
		gosub :DOADDHISTORY
		goto :RUNUSERCOMMANDLINE
	end
	setvar $LSD_ATTEMPT 1

	:LSD_LOGIN_LOOP
	gosub :KILLTHETRIGGERS
	settextlinetrigger NEEDTOLOGIN :NEEDTOLOGIN "Send a corporate memo to login."
	settextlinetrigger BOTSBUSY :BOTSBUSY "- Time Left   = "
	settextlinetrigger BOTSNOTBUSY :BOTSNOTBUSY "= General"
	setdelaytrigger BOTNOTTHERE :BOTNOTTHERE 4000
	send "'"&$LSD_BOTTING&" Status*"
	pause

	:BOTNOTTHERE
	gosub :KILLTHETRIGGERS
	echo "**"&ANSI_14&$LSD_TAGLINEB&ANSI_15&" - "&$LSD_BOTTING&"-bot Is Not Responding**"
	goto :WAIT_FOR_COMMAND

	:NEEDTOLOGIN
	gosub :KILLTHETRIGGERS
	if ($LSD_ATTEMPT <= 3)
		if ($STARTINGLOCATION = "Command")
			send " T T Login***"
		elseif ($STARTINGLOCATION = "Citadel")
			send " X T Login***"
		else
			echo "**"&ANSI_14&$LSD_TAGLINEB&ANSI_15&" - Please Login to Bots!**"
			goto :WAIT_FOR_COMMAND
		end
		setdelaytrigger AREWELOGGEDIN :AREWELOGGEDIN 4000
		settextlinetrigger WELOGGEDIN1 :WELOGGEDIN "- User Verified -"
		settextlinetrigger WELOGGEDIN2 :WELOGGEDIN "- You are logged into this bot"
		echo "**"&ANSI_14&$LSD_TAGLINEB&ANSI_15&" - Waiting For Response (Attempt #"&$LSD_ATTEMPT&") ...**"
		pause

		:AREWELOGGEDIN
		gosub :KILLTHETRIGGERS
		add $LSD_ATTEMPT 1
		goto :LSD_LOGIN_LOOP

		:WELOGGEDIN
		gosub :KILLTHETRIGGERS
		goto :LSD_LOGIN_LOOP
	else
		echo "**"&ANSI_14&$LSD_TAGLINEB&ANSI_15&" - Unable To Login to Bot!!**"
		goto :WAIT_FOR_COMMAND
	end

	:BOTSBUSY
	gosub :KILLTHETRIGGERS
	echo "**"&ANSI_14&$LSD_TAGLINEB&ANSI_15&" - Bot must be in General Mode**"
	goto :WAIT_FOR_COMMAND

	:BOTSNOTBUSY
	gosub :KILLTHETRIGGERS
	settextlinetrigger MODE_RESET :MODE_RESET "All non-system scripts and modules killed, and modes reset."
	setdelaytrigger MODE_ISSUE :MODE_ISSUE 4000
	echo "**"&ANSI_14&$LSD_TAGLINEB&ANSI_15&" - Waiting 4 Seconds For Response...**"
	send "'"&$LSD_BOTTING&" StopAll*"
	pause

	:MODE_ISSUE
	gosub :KILLTHETRIGGERS
	echo "**"&ANSI_14&$LSD_TAGLINEB&ANSI_15&" - StopAll Timed Out. Please Try Again!**"
	goto :WAIT_FOR_COMMAND

	:MODE_RESET
	gosub :KILLTHETRIGGERS
	send "'"&$LSD_BOTTING&" LSD "&$LSD_ORDER&"*"
	goto :WAIT_FOR_COMMAND
end
goto :TOPOFMENU

:PAD_THIS
if ($LSD_STR_PAD < 10)
	setvar $LSD_STR_PAD "     "&$LSD_STR_PAD
elseif ($LSD_STR_PAD < 100)
	setvar $LSD_STR_PAD "    "&$LSD_STR_PAD
elseif ($LSD_STR_PAD < 1000)
	setvar $LSD_STR_PAD "   "&$LSD_STR_PAD
elseif ($LSD_STR_PAD < 10000)
	setvar $LSD_STR_PAD "  "&$LSD_STR_PAD
elseif ($LSD_STR_PAD < 100000)
	setvar $LSD_STR_PAD " "&$LSD_STR_PAD
end
return

:COMMASIZE
if ($LSD_CASHAMOUNT < 1000)
elseif ($LSD_CASHAMOUNT < 1000000)
	getlength $LSD_CASHAMOUNT $LSD_LEN
	setvar $LSD_LEN ($LSD_LEN - 3)
	cuttext $LSD_CASHAMOUNT $LSD_TMP 1 $LSD_LEN
	cuttext $LSD_CASHAMOUNT $LSD_TMP1 ($LSD_LEN + 1) 999
	setvar $LSD_TMP $LSD_TMP&","&$LSD_TMP1
	setvar $LSD_CASHAMOUNT $LSD_TMP
elseif ($LSD_CASHAMOUNT <= 999999999)
	getlength $LSD_CASHAMOUNT $LSD_LEN
	setvar $LSD_LEN ($LSD_LEN - 6)
	cuttext $LSD_CASHAMOUNT $LSD_TMP 1 $LSD_LEN
	setvar $LSD_TMP $LSD_TMP&","
	cuttext $LSD_CASHAMOUNT $LSD_TMP1 ($LSD_LEN + 1) 3
	setvar $LSD_TMP $LSD_TMP&$LSD_TMP1&","
	cuttext $LSD_CASHAMOUNT $LSD_TMP1 ($LSD_LEN + 4) 999
	setvar $LSD_TMP $LSD_TMP&$LSD_TMP1
	setvar $LSD_CASHAMOUNT $LSD_TMP
end
return

:GETITEMINPUT
if ($YES_NO)
	echo #27&"[1A"&#27&"[K"&ANSI_14&"*"&$ITEM_NAME&"                         *"
	getconsoleinput $LSD_SELECTION SINGLEKEY
else
	getinput $LSD_SELECTION ANSI_15&#27&"[1A"&#27&"[K"&ANSI_14&"*"&$ITEM_NAME&" To Buy (M for Maximum)?"
end
uppercase $LSD_SELECTION
if ($LSD_SELECTION = "M")
	setvar $LSD_SELECTION "Max"
elseif ($LSD_SELECTION = "Y")
	setvar $LSD_SELECTION "Yes"
elseif ($LSD_SELECTION = "N")
	setvar $LSD_SELECTION ""
else
	if ($YES_NO)
		setvar $LSD_SELECTION ""
	else
		isnumber $LSD_TST $LSD_SELECTION
		if ($LSD_TST <> 0)
			if ($LSD_SELECTION = 0)
				setvar $LSD_SELECTION ""
			elseif ($LSD_SELECTION > $ITEM_MAX)
				setvar $LSD_SELECTION $ITEM_MAX
			else
				setvar $LSD_SELECTION $LSD_SELECTION
			end
		end
	end
end
return

:PREPAREORDER
if ($YES_NO)
	if ($ITEM_TYPE <> "")
		setvar $LSD_ORDER $LSD_ORDER&"Y"
	else
		setvar $LSD_ORDER $LSD_ORDER&"N"
	end
else
	if ($ITEM_TYPE <> "")
		if ($ITEM_TYPE = "Max")
			setvar $LSD_ORDER $LSD_ORDER&"M"
		else
			setvar $LSD_ORDER $LSD_ORDER&$ITEM_TYPE
		end
	else
		setvar $LSD_ORDER $LSD_ORDER&0
	end
end
setvar $LSD_ORDER $LSD_ORDER&$LSD__PAD
setvar $YES_NO FALSE
return

:CHECKCOSTS
setvar $LSD_COSTSAREGOOD TRUE
loadvar $LSD_LIMPREMOVALCOST
loadvar $LSD_GENCOST
loadvar $LSD_ARMIDCOST
loadvar $LSD_LIMPCOST
loadvar $LSD_BEACON
loadvar $LSD_TWARPICOST
loadvar $LSD_TWARPIICOST
loadvar $LSD_TWARPUPCOST
loadvar $LSD_PSCAN
loadvar $LSD_ATOMICCOST
loadvar $LSD_CORBOCOST
loadvar $LSD_EPROBE
loadvar $LSD_PHOTONCOST
loadvar $LSD_CLOAKCOST
loadvar $LSD_DISRUPTCOST
loadvar $LSD_HOLOCOST
loadvar $LSD_DSCANCOST
loadvar $LSD_REREGISTERCOST
if (($LSD_LIMPREMOVALCOST = 0) or ($LSD_GENCOST = 0) or ($LSD_ARMIDCOST = 0) or ($LSD_LIMPCOST = 0) or ($LSD_BEACON = 0) or ($LSD_TWARPICOST = 0) or ($LSD_TWARPIICOST = 0) or ($LSD_TWARPUPCOST = 0) or ($LSD_PSCAN = 0) or ($LSD_ATOMICCOST = 0) or ($LSD_CORBOCOST = 0) or ($LSD_EPROBE = 0) or ($LSD_PHOTONCOST = 0) or ($LSD_CLOAKCOST = 0) or ($LSD_DISRUPTCOST = 0) or ($LSD_HOLOCOST = 0) or ($LSD_DSCANCOST = 0) or ($LSD_REREGISTERCOST = 0))
	gosub :GAMESTATS
end
return

:PADITEMCOSTS
getlength $LSD_PADTHISCOST $LSD_LEN
if ($LSD_LEN = 1)
	setvar $LSD_PADTHISCOST "      "&$LSD_PADTHISCOST
elseif ($LSD_LEN = 2)
	setvar $LSD_PADTHISCOST "     "&$LSD_PADTHISCOST
elseif ($LSD_LEN = 3)
	setvar $LSD_PADTHISCOST "    "&$LSD_PADTHISCOST
elseif ($LSD_LEN = 4)
	setvar $LSD_PADTHISCOST "   "&$LSD_PADTHISCOST
elseif ($LSD_LEN = 5)
	setvar $LSD_PADTHISCOST "  "&$LSD_PADTHISCOST
elseif ($LSD_LEN = 6)
	setvar $LSD_PADTHISCOST " "&$LSD_PADTHISCOST
else
end
return

:GETCLASS0COSTS
send "CR1*Q  "
waitfor "Commerce report for:"
settextlinetrigger LSD_CARGOHOLDS :LSD_CARGOHOLDS "A  Cargo holds     : "
settextlinetrigger LSD_FIGHTERS :LSD_FIGHTERS "B  Fighters        : "
settextlinetrigger LSD_SHIELDS :LSD_SHIELDS "C  Shield Points   : "
settexttrigger LSD_FINI1 :LSD_FINI "Command [TL="
settexttrigger LSD_FINI2 :LSD_FINI "Citadel command (?"
pause

:LSD_CARGOHOLDS
killtrigger LSD_CARGOHOLDS
getword CURRENTLINE $LSD_HOLDCOST 5
isnumber $LSD_TST $LSD_HOLDCOST
if ($LSD_TST = 0)
	setvar $LSD_HOLDCOST 0
end
pause

:LSD_FIGHTERS
killtrigger LSD_FIGHTERS
getword CURRENTLINE $LSD_FIGHTERCOST 4
isnumber $LSD_TST $LSD_FIGHTERCOST
if ($LSD_TST = 0)
	setvar $LSD_FIGHTERCOST 0
end
pause

:LSD_SHIELDS
killtrigger LSD_SHIELDS
getword CURRENTLINE $LSD_SHIELD 5
isnumber $LSD_TST $LSD_SHIELD
if ($LSD_TST = 0)
	setvar $LSD_SHIELD 0
end
pause

:LSD_FINI
gosub :KILLTHETRIGGERS
setvar $LSD_CASHAMOUNT $LSD_HOLDCOST
gosub :COMMASIZE
setvar $LSD_LSD_HOLDCOST $LSD_CASHAMOUNT
setvar $LSD_CASHAMOUNT $LSD_FIGHTERCOST
gosub :COMMASIZE
setvar $LSD_FIGHTERCOST $LSD_CASHAMOUNT
setvar $LSD_CASHAMOUNT $LSD_SHIELD
gosub :COMMASIZE
setvar $LSD_SHIELD $LSD_CASHAMOUNT
return

:SETMENUECHOS
isnumber $LSD_TST $LSD_NUMBEROFSHIP
if ($LSD_TST <> 0)
	if ($LSD_NUMBEROFSHIP > 0)
		gettext $LSD__TRICKSTER $LSD_COST "^^" "@@"
		striptext $LSD_COST ","
		striptext $LSD_COST " "
		gettext $LSD__TRICKSTER $LSD_TEMP "@@" "!!"
		striptext $LSD_REREGISTERCOST ","
		setvar $LSD_COST ($LSD_COST + $LSD_REREGISTERCOST)
		setvar $LSD_MATHOUT ($LSD_NUMBEROFSHIP * $LSD_COST)
		setvar $LSD__TOTAL ($LSD__TOTAL + $LSD_MATHOUT)
		setvar $LSD_CASHAMOUNT $LSD_MATHOUT
		gosub :COMMASIZE
		setvar $LSD_ECHO_TRICKSTER ANSI_15&$LSD_NUMBEROFSHIP&" "&$LSD_TEMP&ANSI_7&"  ($"&$LSD_CASHAMOUNT&")"
	else
		setvar $LSD_ECHO_TRICKSTER ""
	end
else
	setvar $LSD_ECHO_TRICKSTER ""
end
setvar $ITEM_NUMBER $LSD__ATOMICS
setvar $LSD_COST $LSD_ATOMICCOST
gosub :DOSETMENUECHO
setvar $LSD_ECHO_ATOMICS $ITEM_ECHO
setvar $ITEM_NUMBER $LSD__BEACONS
setvar $LSD_COST $LSD_BEACON
gosub :DOSETMENUECHO
setvar $LSD_ECHO_BEACONS $ITEM_ECHO
setvar $ITEM_NUMBER $LSD__CORBO
setvar $LSD_COST $LSD_CORBOCOST
gosub :DOSETMENUECHO
setvar $LSD_ECHO_CORBO $ITEM_ECHO
setvar $ITEM_NUMBER $LSD__CLOAK
setvar $LSD_COST $LSD_CLOAKCOST
gosub :DOSETMENUECHO
setvar $LSD_ECHO_CLOAK $ITEM_ECHO
setvar $ITEM_NUMBER $LSD__PROBE
setvar $LSD_COST $LSD_EPROBE
gosub :DOSETMENUECHO
setvar $LSD_ECHO_PROBE $ITEM_ECHO
if ($LSD__PSCAN = "Yes")
	setvar $LSD_COST $LSD_PSCAN
	striptext $LSD_COST ","
	setvar $LSD_MATHOUT $LSD_COST
	isnumber $LSD_TST $LSD_NUMBEROFSHIP
	if ($LSD_TST <> 0)
		setvar $LSD_MATHOUT ($LSD_MATHOUT * $LSD_NUMBEROFSHIP)
		setvar $LSD_MULTIPLIER ANSI_8&"(X"&$LSD_NUMBEROFSHIP&")"
	else
		setvar $LSD_MULTIPLIER ""
	end
	setvar $LSD__TOTAL ($LSD__TOTAL + $LSD_MATHOUT)
	setvar $LSD_CASHAMOUNT $LSD_MATHOUT
	gosub :COMMASIZE
	setvar $LSD_ECHO_PSCAN ANSI_15&$LSD__PSCAN&"  "&$LSD_MULTIPLIER&ANSI_7&"($"&$LSD_CASHAMOUNT&")"
else
	setvar $LSD_ECHO_PSCAN ""
end
setvar $ITEM_NUMBER $LSD__LIMPS
setvar $LSD_COST $LSD_LIMPCOST
gosub :DOSETMENUECHO
setvar $LSD_ECHO_LIMPS $ITEM_ECHO
setvar $ITEM_NUMBER $LSD__MINES
setvar $LSD_COST $LSD_ARMIDCOST
gosub :DOSETMENUECHO
setvar $LSD_ECHO_MINES $ITEM_ECHO
setvar $ITEM_NUMBER $LSD__PHOTON
setvar $LSD_COST $LSD_PHOTONCOST
gosub :DOSETMENUECHO
setvar $LSD_ECHO_PHOTON $ITEM_ECHO
if ($LSD__LRSCAN = "Yes")
	setvar $LSD_COST $LSD_HOLOCOST
	striptext $LSD_COST ","
	setvar $LSD_MATHOUT $LSD_COST
	isnumber $LSD_TST $LSD_NUMBEROFSHIP
	if ($LSD_TST <> 0)
		setvar $LSD_MATHOUT ($LSD_MATHOUT * $LSD_NUMBEROFSHIP)
		setvar $LSD_MULTIPLIER ANSI_8&"(X"&$LSD_NUMBEROFSHIP&")"
	else
		setvar $LSD_MULTIPLIER ""
	end
	setvar $LSD__TOTAL ($LSD__TOTAL + $LSD_MATHOUT)
	setvar $LSD_CASHAMOUNT $LSD_MATHOUT
	gosub :COMMASIZE
	setvar $LSD_ECHO_LRSCAN ANSI_15&$LSD__LRSCAN&"  "&$LSD_MULTIPLIER&ANSI_7&"($"&$LSD_CASHAMOUNT&")"
else
	setvar $LSD_ECHO_LRSCAN ""
end
setvar $ITEM_NUMBER $LSD__DISRUPT
setvar $LSD_COST $LSD_DISRUPTCOST
gosub :DOSETMENUECHO
setvar $LSD_ECHO_DISRUPT $ITEM_ECHO
setvar $ITEM_NUMBER $LSD__GENTORP
setvar $LSD_COST $LSD_GENCOST
gosub :DOSETMENUECHO
setvar $LSD_ECHO_GENTORP $ITEM_ECHO
if ($LSD__T2TWARP = "Yes")
	setvar $LSD_COST $LSD_TWARPIICOST
	striptext $LSD_COST ","
	setvar $LSD_MATHOUT $LSD_COST
	isnumber $LSD_TST $LSD_NUMBEROFSHIP
	if ($LSD_TST <> 0)
		setvar $LSD_MATHOUT ($LSD_MATHOUT * $LSD_NUMBEROFSHIP)
		setvar $LSD_MULTIPLIER ANSI_8&"(X"&$LSD_NUMBEROFSHIP&")"
	else
		setvar $LSD_MULTIPLIER ""
	end
	setvar $LSD__TOTAL ($LSD__TOTAL + $LSD_MATHOUT)
	setvar $LSD_CASHAMOUNT $LSD_MATHOUT
	gosub :COMMASIZE
	setvar $LSD_ECHO_T2TWARP ANSI_15&$LSD__T2TWARP&"  "&$LSD_MULTIPLIER&ANSI_7&"($"&$LSD_CASHAMOUNT&")"
else
	setvar $LSD_ECHO_T2TWARP ""
end
setvar $ITEM_NUMBER $LSD__HOLDS
setvar $LSD_COST $LSD_HOLDCOST
gosub :DOSETMENUECHO
setvar $LSD_ECHO_HOLDS $ITEM_ECHO
setvar $ITEM_NUMBER $LSD__FIGS
setvar $LSD_COST $LSD_FIGHTERCOST
gosub :DOSETMENUECHO
setvar $LSD_ECHO_FIGS $ITEM_ECHO
setvar $ITEM_NUMBER $LSD__SHIELDS
setvar $LSD_COST $LSD_SHIELD
gosub :DOSETMENUECHO
setvar $LSD_ECHO_SHIELDS $ITEM_ECHO
return

:DOSETMENUECHO
isnumber $LSD_TST $ITEM_NUMBER
if ($LSD_TST <> 0)
	striptext $LSD_COST ","
	setvar $LSD_MATHOUT ($ITEM_NUMBER * $LSD_COST)
	isnumber $LSD_TST $LSD_NUMBEROFSHIP
	if ($LSD_TST <> 0)
		setvar $LSD_MATHOUT ($LSD_MATHOUT * $LSD_NUMBEROFSHIP)
		setvar $LSD_MULTIPLIER ANSI_8&"(X"&$LSD_NUMBEROFSHIP&")"
	else
		setvar $LSD_MULTIPLIER ""
	end
	setvar $LSD__TOTAL ($LSD__TOTAL + $LSD_MATHOUT)
	setvar $LSD_CASHAMOUNT $LSD_MATHOUT
	gosub :COMMASIZE
	setvar $ITEM_ECHO ANSI_15&$ITEM_NUMBER&"  "&$LSD_MULTIPLIER&ANSI_7&"($"&$LSD_CASHAMOUNT&")"
elseif ($ITEM_NUMBER = "Max")
	setvar $ITEM_ECHO "Max"
else
	setvar $ITEM_ECHO ""
end
return

:LOADSHIPDATA
fileexists $LSD_TEST $LSD_SHIPS_FILE
if ($LSD_TEST)
	setvar $LSD_I 1
	read $LSD_SHIPS_FILE $LSD_LINE $LSD_I
	while (($LSD_LINE <> EOF) and ($LSD_I <= $LSD_SHIPLISTMAX))
		getwordpos $LSD_LINE $LSD_POS #9
		if ($LSD_POS <> 2)
			setvar $LSD_SHIPDATA_VALID FALSE
			return
		end
		cuttext $LSD_LINE $LSD_TEMP 1 1
		setvar $LSD_SHIPLIST[$LSD_I] $LSD_TEMP
		cuttext $LSD_LINE $LSD_LINE2 3 999
		setvar $LSD_LINE $LSD_LINE2
		getwordpos $LSD_LINE $LSD_POS #9
		if ($LSD_POS = 0)
			setvar $LSD_SHIPDATA_VALID FALSE
			return
		end
		cuttext $LSD_LINE $LSD_TEMP1 1 ($LSD_POS - 1)
		setvar $LSD_SHIPLIST[$LSD_I][1] $LSD_TEMP1
		striptext $LSD_LINE $LSD_TEMP1&#9
		getwordpos $LSD_LINE $LSD_POS #9
		if ($LSD_POS = 0)
			setvar $LSD_SHIPDATA_VALID FALSE
			return
		end
		cuttext $LSD_LINE $LSD_TEMP2 1 ($LSD_POS - 1)
		setvar $LSD_SHIPLIST[$LSD_I][2] $LSD_TEMP2
		striptext $LSD_LINE $LSD_TEMP2&#9
		setvar $LSD_SHIPLIST[$LSD_I][3] $LSD_LINE

		:NEXTREALLINE
		add $LSD_I 1
		read $LSD_SHIPS_FILE $LSD_LINE $LSD_I
	end
	setvar $LSD_SHIPDATA_VALID TRUE
else
	setvar $LSD_SHIPDATA_VALID FALSE
end
return

:PARSESHIPDATA
delete $LSD_SHIPS_FILE
setvar $LSD_I 0
send "S B N Y ?"
waitfor "Which ship are you interested in "
settextlinetrigger NEXTPAGE :NEXTPAGE "<+> Next Page"

:NEXTPAGERESET
settextlinetrigger QUIT2LEAVE :QUIT2LEAVE "<Q> To Leave"

:LINETRIGNEXT
settextlinetrigger LINETRIG :LINETRIG
pause

:NEXTPAGE
gosub :KILLTHETRIGGERS
add $LSD_I 1
setvar $LSD_SHIPLIST[$LSD_I] "+"
setvar $LSD_SHIPLIST[$LSD_I][1] "This Inidcates"
setvar $LSD_SHIPLIST[$LSD_I][2] "Another"
setvar $LSD_SHIPLIST[$LSD_I][3] "Page is availble for display"
send "+"
waitfor "Which ship are you interested in "
settextlinetrigger LINETRIG :LINETRIG
settextlinetrigger NEXTPAGE :QUIT2LEAVE "<+> Next Page"
settextlinetrigger QUIT2LEAVE :QUIT2LEAVE "<Q> To Leave"
pause

:QUIT2LEAVE
gosub :KILLTHETRIGGERS
send " Q Q "
waitfor "<StarDock> Where to? (?="
delete $LSD_TSTFILE
setvar $LSD_II 1
while ($LSD_II <= $LSD_I)
	write $LSD_SHIPS_FILE $LSD_SHIPLIST[$LSD_II]&#9&$LSD_SHIPLIST[$LSD_II][1]&#9&$LSD_SHIPLIST[$LSD_II][2]&#9&$LSD_SHIPLIST[$LSD_II][3]
	add $LSD_II 1
end
return

:LINETRIG
setvar $LSD_TEMP CURRENTLINE&"@@@"
if ($LSD_TEMP <> "@@@")
	getwordpos $LSD_TEMP $LSD_POS "<"
	if ($LSD_POS = 1)
		getwordpos $LSD_TEMP $LSD_POS "<Q>"
		if ($LSD_POS = 0)
			add $LSD_I 1
			gettext $LSD_TEMP $LSD_SHIPLIST[$LSD_I] "<" ">"
			gettext $LSD_TEMP $LSD_SHIPLIST[$LSD_I][1] "> " "   "
			gettext $LSD_TEMP $LSD_SHIPLIST[$LSD_I][2] "   " "@@@"
			striptext $LSD_SHIPLIST[$LSD_I][2] " "
			gettext CURRENTANSILINE $LSD_SHIPLIST[$LSD_I][3] "[35m> " "    "
		end
	end
end
goto :LINETRIGNEXT

:DISPLAYMENU
setvar $LSD_LINEWIDTHMAX 45
setvar $LSD_PAGES_EXIST FALSE
setvar $LSD_NUMBEROFSHIP ""
setvar $LSD_I 1

:NEXTPAGEPLEASE
echo #27&"[2J"
echo "***"
if ($ISDOCKSHOPPER)
	echo "     "&ANSI_15&#196&#196&ANSI_7&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_7&#196&ANSI_15&#196&#196
	echo ANSI_14&"*        LoneStar's StarDock Shopper"
	echo ANSI_9&"*         Mind ()ver Matter Edition"
	echo ANSI_15&"*          Emporium Daily Specials"
	echo ANSI_8&"*                Version "&$LSD_CURENT_VERSION&"*"
	echo "     "&ANSI_15&#196&#196&ANSI_7&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_8&#196&ANSI_7&#196&ANSI_15&#196&#196
	echo "*"
end
setarray $LSD_MENUSELECTIONS $LSD_SHIPLISTMAX
while ($LSD_SHIPLIST[$LSD_I] <> 0)
	if ($LSD_SHIPLIST[$LSD_I] <> "+")
		setvar $LSD_SPACES $LSD_LINEWIDTHMAX
		setvar $LSD_ANSI_LINE "  "&ANSI_5&"<"&ANSI_6&$LSD_SHIPLIST[$LSD_I]&ANSI_5&"> "
		setvar $LSD_TEMP $LSD_SHIPLIST[$LSD_I][2]
		striptext $LSD_TEMP ","
		striptext $LSD_TEMP " "
		getlength $LSD_SHIPLIST[$LSD_I][1] $LSD_LEN
		if ($LSD_LEN > ($LSD_LINEWIDTHMAX - 10))
			subtract $LSD_LEN 10
			cuttext $LSD_SHIPLIST[$LSD_I][3] $LSD_TEMP 1 $LSD_LEN
		else
			setvar $LSD_TEMP $LSD_SHIPLIST[$LSD_I][3]
		end
		setvar $LSD_ANSI_LINE $LSD_ANSI_LINE&$LSD_TEMP
		subtract $LSD_SPACES $LSD_LEN
		getlength $LSD_SHIPLIST[$LSD_I][2] $LSD_LEN
		subtract $LSD_SPACES $LSD_LEN
		setvar $LSD_SPACER ""
		while ($LSD_SPACES > 0)
			setvar $LSD_SPACER $LSD_SPACER&" "
			subtract $LSD_SPACES 1
		end
		setvar $LSD_ANSI_LINE $LSD_ANSI_LINE&$LSD_SPACER&ANSI_14&$LSD_SHIPLIST[$LSD_I][2]&"*"
		setvar $LSD_MENUSELECTIONS[$LSD_I] $LSD_SHIPLIST[$LSD_I]
		echo $LSD_ANSI_LINE
	else
		setvar $LSD_PAGES_EXIST TRUE
		setvar $LSD_PAGEIDX $LSD_I
		goto :PAGEDONE
	end
	add $LSD_I 1
end

:PAGEDONE
echo "   " #27 "[1m" ANSI_4 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196
echo "*"
if ($LSD_PAGES_EXIST)
	echo "  "&ANSI_5&"<"&ANSI_6&"+"&ANSI_5&">"&ANSI_6&" NextPage*"
end
echo "  "&ANSI_5&"<"&ANSI_6&"Q"&ANSI_5&">"&ANSI_6&" To Leave*"
echo "*"

:MAKINGANOTHERSLECTION
echo "  "&ANSI_5&"Which ship are you interested in? "
getconsoleinput $LSD_SELECTION SINGLEKEY
uppercase $LSD_SELECTION
if ($LSD_SELECTION = "Q")
	return
elseif ($LSD_PAGES_EXIST and ($LSD_SELECTION = "+"))
	if ($LSD_I = $LSD_PAGEIDX)
		setvar $LSD_PAGETWOSELECTED TRUE
		add $LSD_I 1
	else
		setvar $LSD_PAGETWOSELECTED FALSE
		setvar $LSD_I 1
	end
	goto :NEXTPAGEPLEASE
else
	setvar $LSD_PTR 1
	while ($LSD_PTR <= $LSD_SHIPLISTMAX)
		if ($LSD_MENUSELECTIONS[$LSD_PTR] <> 0)
			if ($LSD_MENUSELECTIONS[$LSD_PTR] = $LSD_SELECTION)
				setprecision 0
				setvar $LSD_NUMBEROFSHIP ""

				:INPUTANOTHERAMOUNT
				getinput $LSD_NUMBEROFSHIP "  "&ANSI_5&"How Many "&$LSD_SHIPLIST[$LSD_PTR][1]&"'s ?"
				isnumber $LSD_TEST $LSD_NUMBEROFSHIP
				if ($LSD_TEST = 0)
					goto :INPUTANOTHERAMOUNT
				end
				if ($LSD_NUMBEROFSHIP < 0)
					setvar $LSD_NUMBEROFSHIP 0
					setvar $LSD__TRICKSTER ""
					goto :INPUTANOTHERAMOUNT
				end
				if ($LSD_NUMBEROFSHIP = 0)
					setvar $LSD__TRICKSTER ""
				else
					if ($LSD_PAGETWOSELECTED)
						setvar $LSD__TRICKSTER "+"&$LSD_SELECTION&"^^"&$LSD_SHIPLIST[$LSD_PTR][2]&"@@"&$LSD_SHIPLIST[$LSD_PTR][3]&"!!"
					else
						setvar $LSD__TRICKSTER $LSD_SELECTION&"^^"&$LSD_SHIPLIST[$LSD_PTR][2]&"@@"&$LSD_SHIPLIST[$LSD_PTR][3]&"!!"
					end
					getinput $LSD_CUSTOMSHIPNAME "  "&ANSI_5&"What do you want to name this ship? (30 chars) "
					if ($LSD_CUSTOMSHIPNAME = "")
						setvar $LSD_CUSTOMSHIPNAME $LSD_SHIPS_NAMES
					else
						setvar $LSD_CUSTOMSHIPNAMETEST $LSD_CUSTOMSHIPNAME
						striptext $LSD_CUSTOMSHIPNAMETEST " "
						if ($LSD_CUSTOMSHIPNAMETEST = "")
							setvar $LSD_CUSTOMSHIPNAME $LSD_SHIPS_NAMES
						else
							getlength $LSD_CUSTOMSHIPNAME $LSD_LEN
							if ($LSD_LEN > 30)
								cuttext $LSD_CUSTOMSHIPNAME $LSD_CUSTOMSHIPNAME 1 30
							end
						end
					end
				end
				return
			end
		end
		add $LSD_PTR 1
	end
end
echo "*"
echo #27&"[1A"&#27&"[2K"
goto :MAKINGANOTHERSLECTION
return

:PSIMACS
killalltriggers
gosub :CURRENT_PROMPT
setvar $VALIDPROMPTS "Citadel"
gosub :CHECKSTARTINGPROMPT
loadvar $PSIMAC_CORP_LIMPET_DROP_AMT
if ($PSIMAC_CORP_LIMPET_DROP_AMT < 1)
	setvar $PSIMAC_CORP_LIMPET_DROP_AMT 3
	savevar $PSIMAC_CORP_LIMPET_DROP_AMT
end
loadvar $PSIMAC_CORP_ARMID_DROP_AMT
if ($PSIMAC_CORP_ARMID_DROP_AMT < 1)
	setvar $PSIMAC_CORP_ARMID_DROP_AMT 1
	savevar $PSIMAC_CORP_ARMID_DROP_AMT
end
loadvar $PSIMAC_CORP_FTR_DROP_AMT
if ($PSIMAC_CORP_FTR_DROP_AMT < 1)
	setvar $PSIMAC_CORP_FTR_DROP_AMT 1
	savevar $PSIMAC_CORP_FTR_DROP_AMT
end
settextlinetrigger GETP :GETP "Planet #"
send "q*c "
pause

:GETP
getword CURRENTLINE $PLANET 2
striptext $PLANET "#"
waiton "Citadel command (?="

:PRINT_THE__PLANET_MENU

:PLANET_MENU_WITHOUT_CLEAR
echo "**"
echo ANSI_15 "                       -=( " ANSI_14 "Psi Planet Macros" ANSI_15 " )=-  *"
echo ANSI_5 " -----------------------------------------------------------------------------*"
echo ANSI_9 #27&"[35m<"&#27&"[32m1"&#27&"[35m> "&ANSI_14&"Lay 1 personal limpet"&ANSI_9&", land         "&ANSI_11&#27&"[35m<"&#27&"[32m5"&#27&"[35m> "&ANSI_14&"Holoscan"&ANSI_9&", land*"
echo #27&"[35m<"&#27&"[32m2"&#27&"[35m> "&ANSI_14&"Lay "&$PSIMAC_CORP_LIMPET_DROP_AMT&" corporate "&ANSI_11&#27&"[35m<"&#27&"[32mL"&#27&"[35m>"&ANSI_14&"impet(s)"&ANSI_9&", land   "&ANSI_11 #27&"[35m<"&#27&"[32m6"&#27&"[35m> "&ANSI_14&"Lift attack*"
echo #27&"[35m<"&#27&"[32m3"&#27&"[35m> "&ANSI_14&"Lay "&$PSIMAC_CORP_ARMID_DROP_AMT&" corporate "&ANSI_11&#27&"[35m<"&#27&"[32mA"&#27&"[35m>"&ANSI_14&"rmid(s)"&ANSI_9&", land    "&ANSI_11 #27&"[35m<"&#27&"[32m7"&#27&"[35m> "&ANSI_14&"Drop "&$PSIMAC_CORP_FTR_DROP_AMT&" corporate "&ANSI_11&#27&"[35m<"&#27&"[32mF"&#27&"[35m>"&ANSI_14&"ighter(s)"&ANSI_9&"*"
echo #27&"[35m<"&#27&"[32m4"&#27&"[35m> "&ANSI_14&"Density scan"&ANSI_9&", land             "&ANSI_11&"     "&#27&"[35m<"&#27&"[32m8"&#27&"[35m> "&ANSI_14&"Launch a mine disrupter"&ANSI_9&", land*"
echo "*"
echo #27&"[35m<"&#27&"[32mB"&#27&"[35m> "&ANSI_14&"Get Xport List"&ANSI_9&", land                " ANSI_11 #27&"[35m<"&#27&"[32mE"&#27&"[35m> "&ANSI_14&"Toggle IG"&ANSI_9&", land " ANSI_11 "*"
echo #27&"[35m<"&#27&"[32mC"&#27&"[35m> "&ANSI_14&"Xport into ship"&ANSI_9&", land               " ANSI_11 #27&"[35m<"&#27&"[32mG"&#27&"[35m> "&ANSI_14&"Swap Planets*"
echo #27&"[35m<"&#27&"[32mD"&#27&"[35m> "&ANSI_14&"Get sector planet list"&ANSI_9&", land " ANSI_11 "*"
echo ANSI_5 " -----------------------------------------------------------------------------**"

:GETPLANETMACROINPUT
echo ANSI_10 "Your choice?*"
getconsoleinput $CHOSEN_OPTION SINGLEKEY
uppercase $CHOSEN_OPTION
killalltriggers

:PROCESS_COMMAND2
if ($CHOSEN_OPTION = 1)
	goto :PERSLIMP
elseif ($CHOSEN_OPTION = 2)
	goto :CORPLIMP
elseif ($CHOSEN_OPTION = 3)
	goto :CORPARM
elseif ($CHOSEN_OPTION = 4)
	gosub :DSCAN2
	goto :WAIT_FOR_COMMAND
elseif ($CHOSEN_OPTION = 5)
	gosub :HSCAN
	goto :WAIT_FOR_COMMAND
elseif ($CHOSEN_OPTION = 6)
	goto :LIFTA
elseif ($CHOSEN_OPTION = 7)
	goto :DROPFIG
elseif ($CHOSEN_OPTION = 8)
	gosub :QUIKSTATS
	if ($MINE_DISRUPTORS > 0)
		getinput $TEST "Sector to disrupt: "
		isnumber $NUMTEST $TEST
		if ($NUMTEST < 1)
			echo ANSI_12 "**Bad sector number!*"
			goto :PLANETMACMENU
		end
		if (($TEST > SECTORS) or ($TEST <= 10))
			echo ANSI_12 "**Bad sector number!*"
			goto :PLANETMACMENU
		end
		send "q q c  w  y"&$TEST&"*  *  *  q  l " $PLANET "* c s*  "
		waiton "Computer command [TL="
		waiton "Citadel command (?=help)"
		goto :WAIT_FOR_COMMAND
	else
		send "'Out of mine disruptors!*"
		waiton "Citadel command (?=help)"
		goto :WAIT_FOR_COMMAND
	end
elseif ($CHOSEN_OPTION = "B")
	send "q q  x* *    l j"&#8&$PLANET&"* c @"
	waiton "Average Interval Lag:"
	goto :WAIT_FOR_COMMAND
elseif ($CHOSEN_OPTION = "C")
	getinput $SHIPNUM "Ship number to xport to: "
	isnumber $NUMTEST $SHIPNUM
	if ($NUMTEST < 1)
		echo ANSI_12 "*Invalid ship number!*"
		goto :WAIT_FOR_COMMAND
	end
	if (($SHIPNUM < 1) or ($SHIPNUM > 65000))
		echo ANSI_12 "*Invalid ship number!*"
		goto :WAIT_FOR_COMMAND
	end
	setvar $MSG ""
	gosub :KILLTHETRIGGERS
	settextlinetrigger TDET_TRG1 :TXPORT_NOTAVAIL2 "That is not an available ship."
	settextlinetrigger TDET_TRG2 :TXPORT_BADRANGE2 "only has a transport range of"
	settextlinetrigger TDET_TRG3 :TXPORT_SECURITY2 "SECURITY BREACH! Invalid Password, unable to link transporters."
	settextlinetrigger TDET_TRG4 :TXPORT_NOACCESS2 "Access denied!"
	settextlinetrigger TDET_TRG5 :TXPORT_XPRTGOOD2 "Security code accepted, engaging transporter control."
	settexttrigger TDET_TRG6 :TXPORT_GO_AHEAD2 "Average Interval Lag:"
	send "q q  x    "&$SHIPNUM&"    *    *    *    l j"&#8&$PLANET&"*  @"
	pause
	goto :PRINT_THE__PLANET_MENU

	:TXPORT_NOTAVAIL2
	setvar $MSG ANSI_12&"**That ship is not available.*"
	pause

	:TXPORT_BADRANGE2
	setvar $MSG ANSI_12&"**That ship is too far away.*"
	pause

	:TXPORT_SECURITY2
	setvar $MSG ANSI_12&"**That ship is passworded.*"
	pause

	:TXPORT_NOACCESS2
	setvar $MSG ANSI_12&"**Cannot access that ship.*"
	pause

	:TXPORT_XPRTGOOD2
	setvar $MSG ANSI_10&"**Xport good!*"
	pause

	:TXPORT_GO_AHEAD2
	gosub :QUIKSTATS
	if ($CURRENT_PROMPT = "Planet")
		send "c "
	end
	gosub :KILLTHETRIGGERS
	echo $MSG
	goto :WAIT_FOR_COMMAND
elseif ($CHOSEN_OPTION = "D")
	send "q q  lj"&#8&$PLANET&"* c @"
	waiton "Average Interval Lag:"
	goto :WAIT_FOR_COMMAND
elseif ($CHOSEN_OPTION = "E")
	send "q q b z y  l j"&#8&$PLANET&"* c @"
	waiton "Average Interval Lag:"
	goto :WAIT_FOR_COMMAND
elseif ($CHOSEN_OPTION = "G")
	getinput $TEST "Planet to Swap to:: "
	isnumber $NUMTEST $TEST
	if ($NUMTEST < 1)
		echo ANSI_12 "**Not a Planet Number!*"
		goto :PLANETMACMENU
	else
		setvar $PSIMAC_PLANET_SWAP "q q l "&$TEST&"*"&$PLANET&"* c"
		send $PSIMAC_PLANET_SWAP
	end
	goto :WAIT_FOR_COMMAND
elseif ($CHOSEN_OPTION = "F")
	getinput $TEST "Fighters to deploy: "
	isnumber $NUMTEST $TEST
	if ($NUMTEST < 1)
		echo ANSI_12 "**Bad fighter count!*"
	elseif ($TEST <= 0)
		setvar $PSIMAC_CORP_FTR_DROP_AMT 1
		savevar $PSIMAC_CORP_FTR_DROP_AMT
	else
		setvar $PSIMAC_CORP_FTR_DROP_AMT $TEST
		savevar $PSIMAC_CORP_FTR_DROP_AMT
	end
	goto :PRINT_THE__PLANET_MENU
elseif ($CHOSEN_OPTION = "L")
	getinput $TEST "Limpets to deploy: "
	isnumber $NUMTEST $TEST
	if ($NUMTEST < 1)
		echo ANSI_12 "**Bad limpet count!*"
	elseif ($TEST > 250)
		setvar $PSIMAC_CORP_LIMPET_DROP_AMT 250
		savevar $PSIMAC_CORP_LIMPET_DROP_AMT
	elseif ($TEST <= 0)
		setvar $PSIMAC_CORP_LIMPET_DROP_AMT 1
		savevar $PSIMAC_CORP_LIMPET_DROP_AMT
	else
		setvar $PSIMAC_CORP_LIMPET_DROP_AMT $TEST
		savevar $PSIMAC_CORP_LIMPET_DROP_AMT
	end
	goto :PRINT_THE__PLANET_MENU
elseif ($CHOSEN_OPTION = "A")
	getinput $TEST "Armids to deploy: "
	isnumber $NUMTEST $TEST
	if ($NUMTEST < 1)
		echo ANSI_12 "**Bad armid count!*"
	elseif ($TEST > 250)
		setvar $PSIMAC_CORP_ARMID_DROP_AMT 250
		savevar $PSIMAC_CORP_ARMID_DROP_AMT
	elseif ($TEST <= 0)
		setvar $PSIMAC_CORP_ARMID_DROP_AMT 1
		savevar $PSIMAC_CORP_ARMID_DROP_AMT
	else
		setvar $PSIMAC_CORP_ARMID_DROP_AMT $TEST
		savevar $PSIMAC_CORP_ARMID_DROP_AMT
	end
	goto :PRINT_THE__PLANET_MENU
else
	goto :WAIT_FOR_COMMAND
end

:PERSLIMP
gosub :QUIKSTATS
if ($LIMPETS > 0)
	send "q q z n h21  *  p z n n * l " $PLANET "* c s* "
	setvar $DEPTYPE "limpets"
	settextlinetrigger TOOMANYPL :TOOMANY "!  You are limited to "
	settextlinetrigger PLCLEAR :PLCLEAR "Done. You have "
	settextlinetrigger ENEMYPL :NOPERDOWN "These mines are not under your control."
	pause
else
	send "'Out of limpets!*"
	waiton "Citadel command (?=help)"
	goto :WAIT_FOR_COMMAND
end

:PLCLEAR
gosub :KILLTHETRIGGERS
waiton "Citadel command (?=help)"
send "s* "
settextlinetrigger PERDOWN :PERDOWN "(Type 2 Limpet) (yours)"
settextlinetrigger NOPERDOWN :NOPERDOWN "Citadel treasury contains"
pause

:PERDOWN
gosub :KILLTHETRIGGERS
send "'Personal Limpet Deployed!*"
waiton "Citadel command (?=help)"
goto :WAIT_FOR_COMMAND

:NOPERDOWN
gosub :KILLTHETRIGGERS
send "'Sector already has enemy limpets present!*"
waiton "Citadel command (?=help)"
goto :WAIT_FOR_COMMAND

:CORPLIMP
gosub :QUIKSTATS
if ($LIMPETS > 0)
	send "q q z n h2z"&$PSIMAC_CORP_LIMPET_DROP_AMT&"* z c *  l " $PLANET "* c s* "
	if ($PSIMAC_CORP_LIMPET_DROP_AMT > 1)
		setvar $DEPTYPE "Limpets"
	else
		setvar $DEPTYPE "Limpet"
	end
	settextlinetrigger TOOMANYCL :TOOMANY "!  You are limited to "
	settextlinetrigger CLCLEAR :CLCLEAR "Done. You have "
	settextlinetrigger ENEMYCL :NOCLDOWN "These mines are not under your control."
	settextlinetrigger NOTENOUGHCL :NOTENOUGH "You don't have that many mines available."
	pause
else
	send "'Out of limpets!*"
	waiton "Citadel command (?=help)"
	goto :WAIT_FOR_COMMAND
end

:CLCLEAR
gosub :KILLTHETRIGGERS
waiton "Citadel command (?=help)"
send "s* "
settextlinetrigger CLDOWN :CLDOWN "(Type 2 Limpet) (belong to your Corp)"
settextlinetrigger NOCLDOWN :NOCLDOWN "Citadel treasury contains"
pause

:CLDOWN
gosub :KILLTHETRIGGERS
send "'"&$PSIMAC_CORP_LIMPET_DROP_AMT&" Corporate "&$DEPTYPE&" Deployed!*"
waiton "Citadel command (?=help)"
goto :WAIT_FOR_COMMAND

:NOCLDOWN
gosub :KILLTHETRIGGERS
send "'Sector already has enemy limpets present!*"
waiton "Citadel command (?=help)"
goto :WAIT_FOR_COMMAND

:CORPARM
gosub :QUIKSTATS
if ($ARMIDS > 0)
	if ($PSIMAC_CORP_ARMID_DROP_AMT > 1)
		setvar $DEPTYPE "Armids"
	else
		setvar $DEPTYPE "Armid"
	end
	send "q q z n h1z"&$PSIMAC_CORP_ARMID_DROP_AMT&" * z c *  l " $PLANET "* c s* "
	settextlinetrigger TOOMANYA :TOOMANY "!  You are limited to "
	settextlinetrigger ACLEAR :ACLEAR "Done. You have "
	settextlinetrigger ENEMYA :NOADOWN "These mines are not under your control."
	settextlinetrigger NOTENOUGHCA :NOTENOUGH "You don't have that many mines available."
	pause
else
	send "'Out of armids!*"
	waiton "Citadel command (?=help)"
	goto :WAIT_FOR_COMMAND
end

:ACLEAR
gosub :KILLTHETRIGGERS
waiton "Citadel command (?=help)"
send "s* "
settextlinetrigger ADOWN :ADOWN "(Type 1 Armid) (belong to your Corp)"
settextlinetrigger NOADOWN :NOADOWN "Citadel treasury contains"
pause

:ADOWN
gosub :KILLTHETRIGGERS
send "'"&$PSIMAC_CORP_ARMID_DROP_AMT&" Corporate"&$DEPTYPE&" Deployed!*"
waiton "Citadel command (?=help)"
goto :WAIT_FOR_COMMAND

:NOADOWN
gosub :KILLTHETRIGGERS
send "'Sector already has enemy armids present!*"
waiton "Citadel command (?=help)"
goto :WAIT_FOR_COMMAND

:DSCAN2
send "q q z n sdzn l " $PLANET "* c  "
waiton "<Enter Citadel>"
waiton "Citadel command (?=help)"
gosub :DISPLAYADJACENTGRIDANSI
return

:HSCAN
send "q q z n s hzn* l " $PLANET "*  c  "
waiton "<Enter Citadel>"
waiton "Citadel command (?=help)"
gosub :DISPLAYADJACENTGRIDANSI
return

:LIFTA
send "q q z n a y y " $SHIP_MAX_ATTACK "* * z n q z n  l " $PLANET "*  m  *** c s* @"
waiton "Average Interval Lag:"
goto :GETPLANETMACROINPUT

:DROPFIG
gosub :QUIKSTATS
if ($FIGHTERS > 0)
	send " q q f z"&$PSIMAC_CORP_FTR_DROP_AMT&"* z c d *  l " $PLANET "* c s* "
	if ($PSIMAC_CORP_FTR_DROP_AMT > 1)
		setvar $DEPTYPE "Fighters"
	else
		setvar $DEPTYPE "Fighter"
	end
	settextlinetrigger TOOMANYFIG :TOOMANY "Too many fighters in your fleet!"
	settextlinetrigger FIGCLEAR :FIGCLEAR " fighter(s) in close support."
	settextlinetrigger ENEMYFIG :NOFIGDOWN "These fighters are not under your control."
	pause
else
	send "'Out of fighters!*"
	waiton "Citadel command (?=help)"
	goto :WAIT_FOR_COMMAND
end

:FIGCLEAR
gosub :KILLTHETRIGGERS
waiton "Citadel command (?=help)"
send "s* "
settextlinetrigger FIGDOWN :FIGDOWN "(belong to your Corp) [Defensive]"
settextlinetrigger NOFIGDOWN :NOFIGDOWN "Citadel treasury contains"
pause

:FIGDOWN
gosub :KILLTHETRIGGERS
send "'"&$PSIMAC_CORP_FTR_DROP_AMT&" Corporate "&$DEPTYPE&" Deployed!*"
setvar $TARGET $CURRENT_SECTOR
gosub :ADDFIGTODATA
waiton "Citadel command (?=help)"
goto :WAIT_FOR_COMMAND

:NOFIGDOWN
gosub :KILLTHETRIGGERS
send "'Sector already has enemy fighters present!*"
waiton "Citadel command (?=help)"
goto :WAIT_FOR_COMMAND

:TOOMANY
gosub :KILLTHETRIGGERS
waiton "<Scan Sector>"
waiton "Citadel command (?=help)"
clientmessage "Ship cannot carry that many "&$DEPTYPE&"!"
clientmessage "No "&$DEPTYPE&" were deployed!"
goto :WAIT_FOR_COMMAND

:NOTENOUGH
gosub :KILLTHETRIGGERS
waiton "<Scan Sector>"
waiton "Citadel command (?=help)"
clientmessage "Ship doesn't have that many "&$DEPTYPE&"!"
clientmessage "No "&$DEPTYPE&" were deployed!"
goto :WAIT_FOR_COMMAND

:DONEPSIMACS
echo #27 "[30D                           " #27 "[30D"
goto :WAIT_FOR_COMMAND

:DOCKKIT
killalltriggers
gosub :CURRENT_PROMPT
setvar $VALIDPROMPTS "<StarDock> <Hardware <Libram <FedPolice> <Shipyards> <Tavern>"
gosub :CHECKSTARTINGPROMPT

:PRINT_THE_MENU
gosub :QUIKSTATS
echo "[2J"

:MENU_WITHOUT_CLEAR
echo "*"
echo ANSI_15 "               -=( " ANSI_12 "Dnyarri's Dock Survival Toolkit" ANSI_15 " )=-  *"
echo ANSI_5 " -----------------------------------------------------------------------------*"
echo ANSI_9 #27&"[35m<"&#27&"[32m1"&#27&"[35m> "&ANSI_14&" display stardock sector"&ANSI_9&", re-dock " #27&"[35m<"&#27&"[32m6"&#27&"[35m> "&ANSI_14&" check twarp lock"&ANSI_9&", re-dock*"
echo #27&"[35m<"&#27&"[32m2"&#27&"[35m> "&ANSI_14&" holoscan"&ANSI_9&", re-dock                " #27&"[35m<"&#27&"[32m7"&#27&"[35m> "&ANSI_14&" twarp out*"
echo #27&"[35m<"&#27&"[32m3"&#27&"[35m> "&ANSI_14&" density scan"&ANSI_9&", re-dock            " #27&"[35m<"&#27&"[32m8"&#27&"[35m> "&ANSI_14&" lock tow"&ANSI_9&", twarp out*"
echo #27&"[35m<"&#27&"[32m4"&#27&"[35m> "&ANSI_14&" get xport list"&ANSI_9&", re-dock          " #27&"[35m<"&#27&"[32m9"&#27&"[35m> "&ANSI_14&" xport"&ANSI_9&", re-dock*"
echo #27&"[35m<"&#27&"[32m5"&#27&"[35m> "&ANSI_14&" get planet list"&ANSI_9&", re-dock         *"
echo "*"
echo #27&"[35m<"&#27&"[32mA"&#27&"[35m> "&ANSI_14&" launch mine disruptor"&ANSI_9&", re-dock   " #27&"[35m<"&#27&"[32mE"&#27&"[35m> "&ANSI_14&" make a planet"&ANSI_9&", re-dock*"
echo #27&"[35m<"&#27&"[32mB"&#27&"[35m> "&ANSI_14&" set avoid"&ANSI_9&",re-dock                " #27&"[35m<"&#27&"[32mF"&#27&"[35m> "&ANSI_14&" land on planet and drop ore"&ANSI_9&", re-dock*"
echo #27&"[35m<"&#27&"[32mC"&#27&"[35m> "&ANSI_14&" clear avoided sector"&ANSI_9&", re-dock    " #27&"[35m<"&#27&"[32mG"&#27&"[35m> "&ANSI_14&" land on planet and take all"&ANSI_9&", re-dock*"
echo #27&"[35m<"&#27&"[32mD"&#27&"[35m> "&ANSI_14&" plot course"&ANSI_9&", re-dock             " #27&"[35m<"&#27&"[32mH"&#27&"[35m> "&ANSI_14&" land on and destroy planet"&ANSI_9&", re-dock*"
echo "*"
echo #27&"[35m<"&#27&"[32mZ"&#27&"[35m> "&ANSI_14&" cloak out*"
echo #27&"[35m<"&#27&"[32mL"&#27&"[35m> "&ANSI_14&" get corpie locations"&ANSI_9&", re-dock*"
echo #27&"[35m<"&#27&"[32mW"&#27&"[35m> "&ANSI_14&" C U Y (enable t-warp)"&ANSI_9&" ,re-dock*"
echo #27&"[35m<"&#27&"[32mT"&#27&"[35m> "&ANSI_14&" toggle cn9"&ANSI_9&", re-dock*"
echo #27&"[35m<"&#27&"[32mO"&#27&"[35m> "&ANSI_14&" Ore Swapper X-port*"
echo ANSI_5 " -----------------------------------------------------------------------------**"
echo ANSI_10 "Your choice?*"
getconsoleinput $CHOSEN_OPTION SINGLEKEY
uppercase $CHOSEN_OPTION
killalltriggers

:PROCESS_COMMAND
if ($CHOSEN_OPTION = 1)
	send "qqq  z  n  dp  s  s "
	waiton "Landing on Federation StarDock."
	waiton "<Shipyards> Your option (?)"
elseif ($CHOSEN_OPTION = 2)
	send "qqq  z  n  sh*  p  s  s "
	waiton "Landing on Federation StarDock."
	gosub :QUIKSTATS
	waiton "<Shipyards> Your option (?)"
elseif ($CHOSEN_OPTION = 3)
	send "qqq  z  n  sdp  s  s "
	waiton "Landing on Federation StarDock."
	waiton "<Shipyards> Your option (?)"
elseif ($CHOSEN_OPTION = 4)
	send "qqq  z  n  x**    p  s  s "
	waiton "Landing on Federation StarDock."
	waiton "<Shipyards> Your option (?)"
elseif ($CHOSEN_OPTION = 5)
	send "qqq  z  n  l*  q  q  z  n  p  s  s "
	waiton "Landing on Federation StarDock."
	waiton "<Shipyards> Your option (?)"
elseif ($CHOSEN_OPTION = 6)
	if ($TWARP = "No")
		echo ANSI_12 "**Cannot T-warp. No Twarp drive!*"
		goto :WAIT_FOR_COMMAND
	elseif ($ORE_HOLDS < 3)
		echo ANSI_12 "**Cannot T-warp. No ore!*"
		goto :WAIT_FOR_COMMAND
	end
	getinput $SECTOR "T-Warp to: "
	isnumber $NUMTEST $SECTOR
	if ($NUMTEST < 1)
		echo ANSI_12 "**Invalid sector number!*"
		goto :WAIT_FOR_COMMAND
	end
	if (($SECTOR < 1) or ($SECTOR > SECTORS))
		echo ANSI_12 "**Invalid sector number!*"
		goto :WAIT_FOR_COMMAND
	end
	setvar $MSG ""
	gosub :KILLTHETRIGGERS
	settextlinetrigger DET_TRG1 :DET_BLND "Do you want to make this jump blind?"
	settextlinetrigger DET_TRG2 :DET_FUEL "You do not have enough Fuel Ore to make the jump."
	settextlinetrigger DET_TRG3 :DET_GOOD "Locating beam pinpointed, TransWarp Locked."
	settextlinetrigger DET_TRG4 :DET_DOCK "Landing on Federation StarDock."
	send "qqq  z  n  m  "&$SECTOR&"  *  yn  *  *  p  s  s "
	pause
	goto :PRINT_THE_MENU

	:DET_BLND
	setvar $MSG ANSI_12&"**No fighter lock exists. Blind warp hazard!!*"
	pause

	:DET_FUEL
	setvar $MSG ANSI_12&"**Not enough ore for that jump!*"
	pause

	:DET_GOOD
	setvar $MSG ANSI_10&"**Fighter lock found. Looks good!*"
	pause

	:DET_DOCK
	waiton "<Shipyards> Your option (?)"
	gosub :KILLTHETRIGGERS
	echo $MSG
	goto :WAIT_FOR_COMMAND
elseif ($CHOSEN_OPTION = 7)
	if ($TWARP = "No")
		echo ANSI_12 "**Cannot T-warp. No Twarp drive!*"
		goto :WAIT_FOR_COMMAND
	elseif ($ORE_HOLDS < 3)
		echo ANSI_12 "**Cannot T-warp. No ore!*"
		goto :WAIT_FOR_COMMAND
	end
	getinput $SECTOR "T-Warp to: "
	isnumber $NUMTEST $SECTOR
	if ($NUMTEST < 1)
		echo ANSI_12 "**Invalid sector number!*"
		goto :WAIT_FOR_COMMAND
	end
	if (($SECTOR < 1) or ($SECTOR > SECTORS))
		echo ANSI_12 "**Invalid sector number!*"
		goto :WAIT_FOR_COMMAND
	end
	send "qqq  z  n  m  "&$SECTOR&"  *  y  y  *  *"
	goto :WAIT_FOR_COMMAND
elseif ($CHOSEN_OPTION = 8)
	if ($TWARP = "No")
		echo ANSI_12 "*Cannot T-warp. No Twarp drive!*"
		goto :WAIT_FOR_COMMAND
	elseif ($ORE_HOLDS < 3)
		echo ANSI_12 "*Cannot T-warp. No ore!*"
		goto :WAIT_FOR_COMMAND
	end
	getinput $SHIPNUM "Ship number to tow: "
	isnumber $NUMTEST $SHIPNUM
	if ($NUMTEST < 1)
		echo ANSI_12 "*Invalid ship number!*"
		goto :WAIT_FOR_COMMAND
	end
	if (($SHIPNUM < 1) or ($SHIPNUM > 65000))
		echo ANSI_12 "*Invalid ship number!*"
		goto :WAIT_FOR_COMMAND
	end
	getinput $SECTOR "T-Warp to: "
	isnumber $NUMTEST $SECTOR
	if ($NUMTEST < 1)
		echo ANSI_12 "*Invalid sector number!*"
		goto :WAIT_FOR_COMMAND
	end
	if (($SECTOR < 1) or ($SECTOR > SECTORS))
		echo ANSI_12 "*Invalid sector number!*"
		goto :WAIT_FOR_COMMAND
	end
	send "qqq  z  n  w  n  *  w  n"&$SHIPNUM&"*  *  m  "&$SECTOR&"  *  y  y  *  *"
	goto :WAIT_FOR_COMMAND
elseif ($CHOSEN_OPTION = 9)
	getinput $SHIPNUM "Ship number to xport to: "
	isnumber $NUMTEST $SHIPNUM
	if ($NUMTEST < 1)
		echo ANSI_12 "*Invalid ship number!*"
		goto :WAIT_FOR_COMMAND
	end
	if (($SHIPNUM < 1) or ($SHIPNUM > 65000))
		echo ANSI_12 "*Invalid ship number!*"
		goto :WAIT_FOR_COMMAND
	end
	setvar $MSG ""
	gosub :KILLTHETRIGGERS
	settextlinetrigger DET_TRG1 :XPORT_NOTAVAIL "That is not an available ship."
	settextlinetrigger DET_TRG2 :XPORT_BADRANGE "only has a transport range of"
	settextlinetrigger DET_TRG3 :XPORT_SECURITY "SECURITY BREACH! Invalid Password, unable to link transporters."
	settextlinetrigger DET_TRG4 :XPORT_NOACCESS "Access denied!"
	settextlinetrigger DET_TRG5 :XPORT_XPRTGOOD "Security code accepted, engaging transporter control."
	settextlinetrigger DET_TRG6 :XPORT_GO_AHEAD "Landing on Federation StarDock."
	send "qqq  z  n  x    "&$SHIPNUM&"    *    *    *    p  s  s "
	pause
	goto :PRINT_THE_MENU

	:XPORT_NOTAVAIL
	setvar $MSG ANSI_12&"**That ship is not available.*"
	pause

	:XPORT_BADRANGE
	setvar $MSG ANSI_12&"**That ship is too far away.*"
	pause

	:XPORT_SECURITY
	setvar $MSG ANSI_12&"**That ship is passworded.*"
	pause

	:XPORT_NOACCESS
	setvar $MSG ANSI_12&"**Cannot access that ship.*"
	pause

	:XPORT_XPRTGOOD
	setvar $MSG ANSI_10&"**Xport good!*"
	pause

	:XPORT_GO_AHEAD
	gosub :QUIKSTATS
	waiton "<Shipyards> Your option (?)"
	gosub :KILLTHETRIGGERS
	echo $MSG
	goto :WAIT_FOR_COMMAND
elseif ($CHOSEN_OPTION = "A")
	getinput $SECTOR "To sector: "
	isnumber $NUMTEST $SECTOR
	if ($NUMTEST < 1)
		echo ANSI_12 "**Invalid sector number!*"
		goto :WAIT_FOR_COMMAND
	end
	if (($SECTOR < 1) or ($SECTOR > SECTORS))
		echo ANSI_12 "**Invalid sector number!*"
		goto :WAIT_FOR_COMMAND
	end
	setvar $MSG ""
	gosub :KILLTHETRIGGERS
	settextlinetrigger DET_TRG1 :DIS_NADJ "That is not an adjacent sector"
	settextlinetrigger DET_TRG2 :DIS_NDIS "You do not have any Mine Disruptors!"
	settextlinetrigger DET_TRG3 :DIS_DONE "Disruptor launched into sector"
	settextlinetrigger DET_TRG4 :DIS_OKAY "Landing on Federation StarDock."
	send "qqq  z  n  c  w  y  "&$SECTOR&"  *  q  q  q  z  n  p  s  h "
	pause

	:DIS_NADJ
	setvar $MSG ANSI_10&"**That sector isn't adjacent to StarDock.*"
	pause

	:DIS_NDIS
	setvar $MSG ANSI_10&"**Out of disruptors.*"
	pause

	:DIS_DONE
	setvar $MSG ANSI_10&"**Disruptor launched!*"
	pause

	:DIS_OKAY
	gosub :QUIKSTATS
	waiton "<Hardware Emporium> So what are you looking for (?)"
	gosub :KILLTHETRIGGERS
	echo $MSG
	goto :WAIT_FOR_COMMAND
elseif ($CHOSEN_OPTION = "B")
	getinput $SECTOR "To sector: "
	isnumber $NUMTEST $SECTOR
	if ($NUMTEST < 1)
		echo ANSI_12 "**Invalid sector number!*"
		goto :WAIT_FOR_COMMAND
	end
	if (($SECTOR < 1) or ($SECTOR > SECTORS))
		echo ANSI_12 "**Invalid sector number!*"
		goto :WAIT_FOR_COMMAND
	end
	send "qqq  z  n  c  v  "&$SECTOR&"*  q  p  s  s "
	waiton "Landing on Federation StarDock."
	waiton "<Shipyards> Your option (?)"
elseif ($CHOSEN_OPTION = "C")
	getinput $SECTOR "To sector: "
	isnumber $NUMTEST $SECTOR
	if ($NUMTEST < 1)
		echo ANSI_12 "**Invalid sector number!*"
		goto :WAIT_FOR_COMMAND
	end
	if (($SECTOR < 1) or ($SECTOR > SECTORS))
		echo ANSI_12 "**Invalid sector number!*"
		goto :WAIT_FOR_COMMAND
	end
	send "qqq  z  n  c  v  0  *  y  n  "&$SECTOR&"*  q  p  s  s "
	waiton "Landing on Federation StarDock."
	waiton "<Shipyards> Your option (?)"
	goto :PRINT_THE_MENU
elseif ($CHOSEN_OPTION = "D")
	getinput $SECTOR "To sector: "
	isnumber $NUMTEST $SECTOR
	if ($NUMTEST < 1)
		echo ANSI_12 "**Invalid sector number!*"
		goto :WAIT_FOR_COMMAND
	end
	if (($SECTOR < 1) or ($SECTOR > SECTORS))
		echo ANSI_12 "**Invalid sector number!*"
		goto :WAIT_FOR_COMMAND
	end
	send "^f*"&$SECTOR&"*q"
	waiton "ENDINTERROG"
elseif ($CHOSEN_OPTION = "E")
	if ($GENESIS > 0)
		send "qqq  z  n  u  y  *  .*  z  c  *  p  s  h "
		waiton "Landing on Federation StarDock."
		gosub :QUIKSTATS
		waiton "<Hardware Emporium> So what are you looking for (?)"
	else
		echo ANSI_12 "**You don't have any Genesis Torps!*"
		goto :WAIT_FOR_COMMAND
	end
elseif ($CHOSEN_OPTION = "F")
	if ($ORE_HOLDS < 1)
		echo ANSI_12 "**You have no ore to drop!*"
		goto :WAIT_FOR_COMMAND
	end
	getinput $PNUM "Planet number: "
	isnumber $NUMTEST $PNUM
	if ($NUMTEST < 1)
		echo ANSI_12 "**Invalid planet number!*"
		goto :WAIT_FOR_COMMAND
	end
	if (($PNUM < 1) or ($PNUM > 33000))
		echo ANSI_12 "**Invalid planet number!*"
		goto :WAIT_FOR_COMMAND
	end
	setvar $MSG ""
	gosub :KILLTHETRIGGERS
	settextlinetrigger DET_TRG1 :PLAND_TRG_1 "Engage the Autopilot?"
	settextlinetrigger DET_TRG2 :PLAND_TRG_2 "That planet is not in this sector."
	settextlinetrigger DET_TRG3 :PLAND_TRG_3 "<Take all>"
	settextlinetrigger DET_TRG4 :PLAND_TRG_4 "<Take/Leave Products>"
	settextlinetrigger DET_TRG5 :PLAND_TRG_5 "Landing on Federation StarDock."
	send "qqq  z  n  l "&$PNUM&"  *  *  z  n  z  n  *  z  q  t  n  z  l  1  *  q  q  z  n  p  s  h "
	pause
elseif ($CHOSEN_OPTION = "G")
	getinput $PNUM "Planet number: "
	isnumber $NUMTEST $PNUM
	if ($NUMTEST < 1)
		echo ANSI_12 "**Invalid planet number!*"
		goto :WAIT_FOR_COMMAND
	end
	if (($PNUM < 1) or ($PNUM > 33000))
		echo ANSI_12 "**Invalid planet number!*"
		goto :WAIT_FOR_COMMAND
	end
	setvar $MSG ""
	gosub :KILLTHETRIGGERS
	settextlinetrigger DET_TRG1 :PLAND_TRG_1 "Engage the Autopilot?"
	settextlinetrigger DET_TRG2 :PLAND_TRG_2 "That planet is not in this sector."
	settextlinetrigger DET_TRG3 :PLAND_TRG_3 "<Take all>"
	settextlinetrigger DET_TRG4 :PLAND_TRG_4 "<Take/Leave Products>"
	settextlinetrigger DET_TRG5 :PLAND_TRG_5 "Landing on Federation StarDock."
	send "qqq  z  n  l "&$PNUM&"  *  *  z  n  z  n  *  z  q  a  *  q  q  z  n  p  s  h "
	pause
elseif ($CHOSEN_OPTION = "H")
	if ($ATOMIC < 1)
		echo ANSI_12 "**You don't have any Atomic Dets!*"
		goto :WAIT_FOR_COMMAND
	end
	getinput $PNUM "Planet number: "
	isnumber $NUMTEST $PNUM
	if ($NUMTEST < 1)
		echo ANSI_12 "**Invalid planet number!*"
		goto :WAIT_FOR_COMMAND
	end
	if (($PNUM < 1) or ($PNUM > 33000))
		echo ANSI_12 "**Invalid planet number!*"
		goto :WAIT_FOR_COMMAND
	end
	setvar $MSG ""
	gosub :KILLTHETRIGGERS
	settextlinetrigger DET_TRG1 :PLAND_TRG_1 "Engage the Autopilot?"
	settextlinetrigger DET_TRG2 :PLAND_TRG_2 "That planet is not in this sector."
	settextlinetrigger DET_TRG3 :PLAND_TRG_3 "<Take all>"
	settextlinetrigger DET_TRG4 :PLAND_TRG_4 "<Take/Leave Products>"
	settextlinetrigger DET_TRG5 :PLAND_TRG_5 "Landing on Federation StarDock."
	settextlinetrigger DET_TRG6 :PLAND_TRG_6 "<DANGER> Are you sure you want to do this?"
	send "qqq  z  n  l "&$PNUM&"  *  *  z  n  z  n  *  z  d  y  p  s  h "
	pause
elseif ($CHOSEN_OPTION = "Z")
	if ($CLOAKS > 0)
		echo ANSI_11 "*Are you sure you want to cloak out? (y/N)*"
		getconsoleinput $CHOICE SINGLEKEY
		uppercase $CHOICE
		if ($CHOICE = "Y")
			goto :CLOAK_ON_OUT
		else
			echo ANSI_12&"**Aborting cloak-out.*"
			goto :WAIT_FOR_COMMAND
		end

		:CLOAK_ON_OUT
		send "qqq  y  y"
		goto :WAIT_FOR_COMMAND
	else
		echo ANSI_12&"**You have no cloaking devices!*"
	end
elseif ($CHOSEN_OPTION = "L")
	send "qqq  z  n  t  aq  p  s  s "
	waiton "Landing on Federation StarDock."
	waiton "<Shipyards> Your option (?)"
elseif ($CHOSEN_OPTION = "T")
	send "qqq  z  n  c  n  9q  q  p  s  s "
	waiton "Landing on Federation StarDock."
	waiton "<Shipyards> Your option (?)"
elseif ($CHOSEN_OPTION = "W")
	send "qqq  z  n  c  u  y  q  p  s  s "
	waiton "Landing on Federation StarDock."
	waiton "<Shipyards> Your option (?)"
elseif ($CHOSEN_OPTION = "O")
	goto :SWAP_ORE
end
goto :WAIT_FOR_COMMAND

:SWAP_ORE
echo "**"
echo ANSI_11 "This automates the process of trading ore between ships.**"
echo ANSI_15 "It pops a planet, drops ore and re-docks.*"
echo ANSI_15 "After a brief pause it then lifts, xports, grabs the ore and re-docks.*"
echo ANSI_15 "The result... you're in your new ship, safe at dock w/ ore.*"
echo ANSI_15 "It tries to be as safe as possible but there's always some risk.*"
echo "*"
echo ANSI_14 "Are you sure you want to start the Ore Swapper X-port? (y/N)*"
getconsoleinput $CHOICE SINGLEKEY
uppercase $CHOICE
if ($CHOICE = "Y")
	goto :INIT_ORE_SWAP_VARS
else
	echo ANSI_12&"**Aborting Ore Swapper X-port.*"
	goto :WAIT_FOR_COMMAND
end

:INIT_ORE_SWAP_VARS
setvar $FUNKY_COUNTER 0
getinput $SHIPNUM "Ship number to transfer fuel to: "
isnumber $NUMTEST $SHIPNUM
if ($NUMTEST < 1)
	echo ANSI_12 "*Invalid ship number!*"
	goto :WAIT_FOR_COMMAND
end
if (($SHIPNUM < 1) or ($SHIPNUM > 65000))
	echo ANSI_12 "*Invalid ship number!*"
	goto :WAIT_FOR_COMMAND
end

:TOP_OF_ORE_SWAP
gosub :QUIKSTATS
add $FUNKY_COUNTER 1
if ($GENESIS < 1)
	echo ANSI_12 "**Out of Genesis Torps. You're going to need one for this.*"
	goto :WAIT_FOR_COMMAND
end
if ($ORE_HOLDS < 3)
	echo ANSI_12 "**There's no ore on your ship! You can't drop ore if you don't have any.*"
	goto :WAIT_FOR_COMMAND
end
send "qqq  z  n  u  y  *  .*  z  c  *  p  s  h "
waiton "Landing on Federation StarDock."
getrnd $RAND_WAIT 100 300
killtrigger SAFETY_DELAY
setdelaytrigger SAFETY_DELAY :LIFT_STUFF $RAND_WAIT
pause

:LIFT_STUFF
send "qqq  z  n  l*  *  z  q  t  n  z  l  1  *  q  q  z  n  p  s  h "
gosub :KILLTHETRIGGERS
settextlinetrigger RESULT_TRG1 :RES_TORPS "You don't have any Genesis Torpedoes to launch!"
settextlinetrigger RESULT_TRG2 :RES_NOPLN "There isn't a planet in this sector."
settextlinetrigger RESULT_TRG3 :RES_MLTPL "Registry# and Planet Name"
settextlinetrigger RESULT_TRG4 :RES_LANDD "Landing sequence engaged..."
settextlinetrigger RESULT_TRG5 :RES_BACKD "Landing on Federation StarDock."
pause

:RES_TORPS
echo ANSI_12 "**You somehow ran out of Genesis Torps before launching. This should not have happened! Check your status!*"
send "? "
goto :WAIT_FOR_COMMAND

:RES_NOPLN
echo ANSI_12 "**The planet is gone! Someone might be messing with us.*"
if ($FUNKY_COUNTER < 4)
	goto :TOP_OF_ORE_SWAP
else
	echo ANSI_12 "**I've tried this 3 times, something is definately going on. Check your status!*"
	send "? "
	goto :WAIT_FOR_COMMAND
end

:RES_LANDD
waiton "Planet #"
getword CURRENTLINE $PNUM 2
striptext $PNUM "#"
waiton "(?="
echo ANSI_10 "**We've landed and dropped our ore on planet #"&$PNUM&"!*"
pause

:RES_MLTPL
waiton "--------------------"
gosub :KILLTHETRIGGERS
setvar $P_ARRAY_IDX 0
setarray $P_ARRAY 255
gosub :KILLTHETRIGGERS
settextlinetrigger PLIST_TRIG :PLIST_LINE ">"
settextlinetrigger PLIST_END :PLIST_END "Land on which planet"
pause
goto :WAIT_FOR_COMMAND

:PLIST_LINE
add $P_ARRAY_IDX 1
setvar $LINE CURRENTLINE
striptext $LINE "<"
striptext $LINE ">"
getword $LINE $A_NUMBER 1
setvar $P_ARRAY[$P_ARRAY_IDX] $A_NUMBER
killtrigger PLIST_TRIG
settextlinetrigger PLIST_TRIG :PLIST_LINE "<"
pause
goto :WAIT_FOR_COMMAND

:PLIST_END
gosub :KILLTHETRIGGERS
if ($P_ARRAY_IDX < 1)
	echo ANSI_12 "**The planet is gone! Someone might be messing with us.*"
	if ($FUNKY_COUNTER < 4)
		goto :TOP_OF_ORE_SWAP
	else
		echo ANSI_12 "**I've tried this 3 times, something is definately going on. Check your status!*"
		send "? "
		goto :WAIT_FOR_COMMAND
	end
end
waiton "Landing on Federation StarDock."
waiton "<Hardware Emporium> So what are you looking for (?)"
getrnd $RAND_WAIT 100 300
killtrigger SAFETY_DELAY
setdelaytrigger SAFETY_DELAY :MORE_LIFT_STUFF $RAND_WAIT
pause

:MORE_LIFT_STUFF
getrnd $RND_IDX 1 $P_ARRAY_IDX
setvar $PNUM $P_ARRAY[$RND_IDX]
gosub :KILLTHETRIGGERS
settextlinetrigger RESULT_TRG1 :RES_BADDD "Engage the Autopilot?"
settextlinetrigger RESULT_TRG2 :RES_BADDD "That planet is not in this sector."
settextlinetrigger RESULT_TRG3 :RES_LAND2 "<Take/Leave Products>"
settextlinetrigger RESULT_TRG4 :RES_BACKD "Landing on Federation StarDock."
send "qqq  z  n  l "&$PNUM&"  *  *  z  n  z  n  *  z  q  t  n  z  l  1  *  q  q  z  n  p  s  h "
pause

:RES_BADDD
gosub :KILLTHETRIGGERS
echo ANSI_12 "**Our planet is gone! Someone might be messing with us.*"
if ($FUNKY_COUNTER < 4)
	goto :TOP_OF_ORE_SWAP
else
	echo ANSI_12 "**I've tried this 3 times, something is definately going on. Check your status!*"
	send "? "
end
goto :WAIT_FOR_COMMAND

:RES_LAND2
echo ANSI_10 "**We've landed and dropped our ore on planet #"&$PNUM&"!*"
pause

:RES_BACKD
gosub :KILLTHETRIGGERS
gosub :QUIKSTATS
waiton "<Hardware Emporium> So what are you looking for (?)"
getrnd $RAND_WAIT 100 300
killtrigger SAFETY_DELAY
setdelaytrigger SAFETY_DELAY :YET_MORE_LIFT_STUFF $RAND_WAIT
pause

:YET_MORE_LIFT_STUFF
setvar $MSG ""
settextlinetrigger RESULT_TRG1 :SWAP_XPORT_NOTAVAIL "That is not an available ship."
settextlinetrigger RESULT_TRG2 :SWAP_XPORT_BADRANGE "only has a transport range of"
settextlinetrigger RESULT_TRG3 :SWAP_XPORT_SECURITY "SECURITY BREACH! Invalid Password, unable to link transporters."
settextlinetrigger RESULT_TRG4 :SWAP_XPORT_NOACCESS "Access denied!"
settextlinetrigger RESULT_TRG5 :SWAP_XPORT_XPRTGOOD "Security code accepted, engaging transporter control."
settextlinetrigger RESULT_TRG6 :SWAP_PLAND_NOPLNET1 "Engage the Autopilot?"
settextlinetrigger RESULT_TRG7 :SWAP_PLAND_NOPLNET2 "That planet is not in this sector."
settextlinetrigger RESULT_TRG8 :SWAP_PLAND_NOPLNET3 "Invalid registry number, landing aborted."
settextlinetrigger RESULT_TRG9 :SWAP_PLAND_PRODTAKN "<Take all>"
settextlinetrigger RESULT_TRG0 :SWAP_PLAND_COMPLETE "Landing on Federation StarDock."
send "qqq  z  n  "
send "x    "&$SHIPNUM&"    *    *    *   "
send "l "&$PNUM&"  *  *  z  n  z  n  *  z  q  a  *  q  q  z  n  "
send "p  s  h "
pause

:SWAP_XPORT_NOTAVAIL
setvar $MSG $MSG&ANSI_12&"*That ship is not available, using the original ship...*"
pause

:SWAP_XPORT_BADRANGE
setvar $MSG $MSG&ANSI_12&"*That ship is too far away, using the original ship...*"
pause

:SWAP_XPORT_SECURITY
setvar $MSG $MSG&ANSI_12&"*That ship is passworded, using the original ship...*"
pause

:SWAP_XPORT_NOACCESS
setvar $MSG $MSG&ANSI_12&"*Cannot access that ship, using the original ship...*"
pause

:SWAP_XPORT_XPRTGOOD
setvar $MSG $MSG&ANSI_10&"*Xport good!*"
pause

:SWAP_PLAND_NOPLNET1
setvar $MSG $MSG&ANSI_12&"*The planet has gone missing. Check your status!*"
pause

:SWAP_PLAND_NOPLNET2
setvar $MSG $MSG&ANSI_12&"*The planet has gone missing. Check your status!*"
pause

:SWAP_PLAND_NOPLNET3
setvar $MSG $MSG&ANSI_12&"*The planet has gone missing. Check your status!*"
pause

:SWAP_PLAND_PRODTAKN
setvar $MSG $MSG&ANSI_10&"*Products collected!*"
pause

:SWAP_PLAND_COMPLETE
gosub :KILLTHETRIGGERS
gosub :QUIKSTATS
waiton "<Hardware Emporium> So what are you looking for (?)"
echo $MSG
goto :WAIT_FOR_COMMAND
pause
goto :WAIT_FOR_COMMAND

:PLAND_TRG_1
setvar $MSG ANSI_12&"**There are no planets in the StarDock sector!*"
pause

:PLAND_TRG_2
setvar $MSG ANSI_12&"**That planet is not in the StarDock sector!*"
pause

:PLAND_TRG_3
setvar $MSG ANSI_10&"**Products taken!*"
pause

:PLAND_TRG_4
setvar $MSG ANSI_10&"**Fuel dropped!*"
pause

:PLAND_TRG_6
setvar $MSG ANSI_10&"**Planet destroyed!*"
pause

:PLAND_TRG_5
gosub :QUIKSTATS
waiton "<Hardware Emporium> So what are you looking for (?)"
gosub :KILLTHETRIGGERS
echo $MSG
goto :WAIT_FOR_COMMAND

:DONEDOCKKIT
echo #27 "[30D                        " #27 "[30D"
goto :WAIT_FOR_COMMAND

:TERRAKIT
killalltriggers
gosub :CURRENT_PROMPT
setvar $VALIDPROMPTS "Do How"
gosub :CHECKSTARTINGPROMPT

:PRINT_THE__TERRA_MENU
gosub :QUIKSTATS
echo "[2J"

:TERRA_MENU_WITHOUT_CLEAR
echo "*"
echo ANSI_15 "               -=( " ANSI_12 "M()M Terra Survival Toolkit" ANSI_15 " )=-  "&ANSI_7&"*"
echo ANSI_5 " -----------------------------------------------------------------------------"&ANSI_7&"*"
echo ANSI_9&#27&"[35m<"&#27&"[32m1"&#27&"[35m> "&ANSI_14&" display Terra sector"&ANSI_9&", land       " #27&"[35m<"&#27&"[32m5"&#27&"[35m> "&ANSI_14&" check twarp lock"&ANSI_9&", land*"
echo #27&"[35m<"&#27&"[32m2"&#27&"[35m> "&ANSI_14&" holoscan"&ANSI_9&", land                   " #27&"[35m<"&#27&"[32m6"&#27&"[35m> "&ANSI_14&" lift, twarp out*"
echo #27&"[35m<"&#27&"[32m3"&#27&"[35m> "&ANSI_14&" density scan"&ANSI_9&", land               " #27&"[35m<"&#27&"[32m7"&#27&"[35m> "&ANSI_14&" lift, lock tow"&ANSI_9&", twarp out*"
echo #27&"[35m<"&#27&"[32m4"&#27&"[35m> "&ANSI_14&" get xport list"&ANSI_9&", land             " #27&"[35m<"&#27&"[32m8"&#27&"[35m> "&ANSI_14&" xport"&ANSI_9&", land*"
echo "*"
echo #27&"[35m<"&#27&"[32mA"&#27&"[35m> "&ANSI_14&" set avoid"&ANSI_9&",land                   " #27&"[35m<"&#27&"[32mE"&#27&"[35m> "&ANSI_14&" lift, cloak out*"
echo #27&"[35m<"&#27&"[32mB"&#27&"[35m> "&ANSI_14&" clear avoided sector"&ANSI_9&", land       " #27&"[35m<"&#27&"[32mF"&#27&"[35m> "&ANSI_14&" C U Y (enable t-warp)"&ANSI_9&" ,land*"
echo #27&"[35m<"&#27&"[32mC"&#27&"[35m> "&ANSI_14&" plot course"&ANSI_9&", land                " #27&"[35m<"&#27&"[32mG"&#27&"[35m> "&ANSI_14&" toggle cn9"&ANSI_9&", land*"
echo #27&"[35m<"&#27&"[32mD"&#27&"[35m> "&ANSI_14&" get corpie locations"&ANSI_9&", land       *"
echo ANSI_5 " -----------------------------------------------------------------------------**"
echo ANSI_10 "Your choice?*"
getconsoleinput $CHOSEN_OPTION SINGLEKEY
uppercase $CHOSEN_OPTION
killalltriggers

:PROCESS_COMMAND
if ($CHOSEN_OPTION = 1)
	send "* * dl 1*  "
	gosub :QUIKSTATS
elseif ($CHOSEN_OPTION = 2)
	send "* * shl 1*   "
	gosub :QUIKSTATS
elseif ($CHOSEN_OPTION = 3)
	send "* * sdl 1*  "
	gosub :QUIKSTATS
elseif ($CHOSEN_OPTION = 4)
	send "* *  x**    l 1*  "
	gosub :QUIKSTATS
elseif ($CHOSEN_OPTION = 5)
	if ($TWARP = "No")
		echo ANSI_12 "**Cannot T-warp. No Twarp drive!*"
		goto :WAIT_FOR_COMMAND
	elseif ($ORE_HOLDS < 3)
		echo ANSI_12 "**Cannot T-warp. No ore!*"
		goto :WAIT_FOR_COMMAND
	end
	getinput $SECTOR "T-Warp to: "
	isnumber $NUMTEST $SECTOR
	if ($NUMTEST < 1)
		echo ANSI_12 "**Invalid sector number!*"
		goto :WAIT_FOR_COMMAND
	end
	if (($SECTOR < 1) or ($SECTOR > SECTORS))
		echo ANSI_12 "**Invalid sector number!*"
		goto :WAIT_FOR_COMMAND
	end
	setvar $MSG ""
	gosub :KILLTHETRIGGERS
	settextlinetrigger TDET_TRG1 :TDET_BLND "Do you want to make this jump blind?"
	settextlinetrigger TDET_TRG2 :TDET_FUEL "You do not have enough Fuel Ore to make the jump."
	settextlinetrigger TDET_TRG3 :TDET_GOOD "Locating beam pinpointed, TransWarp Locked."
	settexttrigger TDET_TRG4 :TDET_DOCK "Do you wish to (L)eave or (T)ake Colonists?"
	send "* *   m  "&$SECTOR&"  *  y*  *  *  l 1*   "
	pause
	goto :PRINT_THE_MENU

	:TDET_BLND
	setvar $MSG ANSI_12&"**No fighter lock exists. Blind warp hazard!!*"
	pause

	:TDET_FUEL
	setvar $MSG ANSI_12&"**Not enough ore for that jump!*"
	pause

	:TDET_GOOD
	setvar $MSG ANSI_10&"**Fighter lock found. Looks good!*"
	pause

	:TDET_DOCK
	gosub :QUIKSTATS
	gosub :KILLTHETRIGGERS
	echo $MSG
	goto :WAIT_FOR_COMMAND
elseif ($CHOSEN_OPTION = 6)
	if ($TWARP = "No")
		echo ANSI_12 "**Cannot T-warp. No Twarp drive!*"
		goto :WAIT_FOR_COMMAND
	elseif ($ORE_HOLDS < 3)
		echo ANSI_12 "**Cannot T-warp. No ore!*"
		goto :WAIT_FOR_COMMAND
	end
	getinput $SECTOR "T-Warp to: "
	isnumber $NUMTEST $SECTOR
	if ($NUMTEST < 1)
		echo ANSI_12 "**Invalid sector number!*"
		goto :WAIT_FOR_COMMAND
	end
	if (($SECTOR < 1) or ($SECTOR > SECTORS))
		echo ANSI_12 "**Invalid sector number!*"
		goto :WAIT_FOR_COMMAND
	end
	send "* *  m  "&$SECTOR&"  *  y  y  *  *"
	gosub :QUIKSTATS
	if ($CURRENT_SECTOR = 1)
		send "l 1*  "
	end
	goto :WAIT_FOR_COMMAND
elseif ($CHOSEN_OPTION = 7)
	if ($TWARP = "No")
		echo ANSI_12 "*Cannot T-warp. No Twarp drive!*"
		goto :WAIT_FOR_COMMAND
	elseif ($ORE_HOLDS < 3)
		echo ANSI_12 "*Cannot T-warp. No ore!*"
		goto :WAIT_FOR_COMMAND
	end
	getinput $SHIPNUM "Ship number to tow: "
	isnumber $NUMTEST $SHIPNUM
	if ($NUMTEST < 1)
		echo ANSI_12 "*Invalid ship number!*"
		goto :WAIT_FOR_COMMAND
	end
	if (($SHIPNUM < 1) or ($SHIPNUM > 65000))
		echo ANSI_12 "*Invalid ship number!*"
		goto :WAIT_FOR_COMMAND
	end
	getinput $SECTOR "T-Warp to: "
	isnumber $NUMTEST $SECTOR
	if ($NUMTEST < 1)
		echo ANSI_12 "*Invalid sector number!*"
		goto :WAIT_FOR_COMMAND
	end
	if (($SECTOR < 1) or ($SECTOR > SECTORS))
		echo ANSI_12 "*Invalid sector number!*"
		goto :WAIT_FOR_COMMAND
	end
	send "* * w  *  *  w  *"&$SHIPNUM&"*  *  m  "&$SECTOR&"  *  y  y  *  *"
	gosub :QUIKSTATS
	if ($CURRENT_SECTOR = 1)
		send "l 1*  "
	end
	goto :WAIT_FOR_COMMAND
elseif ($CHOSEN_OPTION = 8)
	getinput $SHIPNUM "Ship number to xport to: "
	isnumber $NUMTEST $SHIPNUM
	if ($NUMTEST < 1)
		echo ANSI_12 "*Invalid ship number!*"
		goto :WAIT_FOR_COMMAND
	end
	if (($SHIPNUM < 1) or ($SHIPNUM > 65000))
		echo ANSI_12 "*Invalid ship number!*"
		goto :WAIT_FOR_COMMAND
	end
	setvar $MSG ""
	gosub :KILLTHETRIGGERS
	settextlinetrigger TDET_TRG1 :TXPORT_NOTAVAIL "That is not an available ship."
	settextlinetrigger TDET_TRG2 :TXPORT_BADRANGE "only has a transport range of"
	settextlinetrigger TDET_TRG3 :TXPORT_SECURITY "SECURITY BREACH! Invalid Password, unable to link transporters."
	settextlinetrigger TDET_TRG4 :TXPORT_NOACCESS "Access denied!"
	settextlinetrigger TDET_TRG5 :TXPORT_XPRTGOOD "Security code accepted, engaging transporter control."
	settexttrigger TDET_TRG6 :TXPORT_GO_AHEAD "Do you wish to (L)eave or (T)ake Colonists?"
	settexttrigger TDET_TRG7 :TXPORT_GO_AHEAD "That planet is not in this sector."
	settexttrigger TDET_TRG8 :TXPORT_GO_AHEAD "Are you sure you want to jettison all cargo? (Y/N)"
	send "* *  x    z"&$SHIPNUM&"*  *    l j"&#8&" 1*  "
	pause
	goto :PRINT_THE_MENU

	:TXPORT_NOTAVAIL
	setvar $MSG ANSI_12&"**That ship is not available.*"
	pause

	:TXPORT_BADRANGE
	setvar $MSG ANSI_12&"**That ship is too far away.*"
	pause

	:TXPORT_SECURITY
	setvar $MSG ANSI_12&"**That ship is passworded.*"
	pause

	:TXPORT_NOACCESS
	setvar $MSG ANSI_12&"**Cannot access that ship.*"
	pause

	:TXPORT_XPRTGOOD
	setvar $MSG ANSI_10&"**Xport good!*"
	pause

	:TXPORT_GO_AHEAD
	gosub :KILLTHETRIGGERS
	echo $MSG
	goto :WAIT_FOR_COMMAND
elseif ($CHOSEN_OPTION = "A")
	getinput $SECTOR "To sector: "
	isnumber $NUMTEST $SECTOR
	if ($NUMTEST < 1)
		echo ANSI_12 "**Invalid sector number!*"
		goto :WAIT_FOR_COMMAND
	end
	if (($SECTOR < 1) or ($SECTOR > SECTORS))
		echo ANSI_12 "**Invalid sector number!*"
		goto :WAIT_FOR_COMMAND
	end
	send "* *  c  v  "&$SECTOR&"*  q  l 1*  "
	gosub :QUIKSTATS
elseif ($CHOSEN_OPTION = "B")
	getinput $SECTOR "To sector: "
	isnumber $NUMTEST $SECTOR
	if ($NUMTEST < 1)
		echo ANSI_12 "**Invalid sector number!*"
		goto :WAIT_FOR_COMMAND
	end
	if (($SECTOR < 1) or ($SECTOR > SECTORS))
		echo ANSI_12 "**Invalid sector number!*"
		goto :WAIT_FOR_COMMAND
	end
	send "* *  c  v  0  *  y  n  "&$SECTOR&"*  q  l 1*  "
	gosub :QUIKSTATS
elseif ($CHOSEN_OPTION = "C")
	getinput $SECTOR "To sector: "
	isnumber $NUMTEST $SECTOR
	if ($NUMTEST < 1)
		echo ANSI_12 "**Invalid sector number!*"
		goto :WAIT_FOR_COMMAND
	end
	if (($SECTOR < 1) or ($SECTOR > SECTORS))
		echo ANSI_12 "**Invalid sector number!*"
		goto :WAIT_FOR_COMMAND
	end
	send "^f*"&$SECTOR&"*q"
	waiton "ENDINTERROG"
elseif ($CHOSEN_OPTION = "E")
	if ($CLOAKS > 0)
		echo ANSI_11 "*Are you sure you want to cloak out? (y/N)*"
		getconsoleinput $CHOICE SINGLEKEY
		uppercase $CHOICE
		if ($CHOICE = "Y")
			send "* * q  y  y"
		else
			echo ANSI_12&"**Aborting cloak-out.*"
			goto :WAIT_FOR_COMMAND
		end
		goto :WAIT_FOR_COMMAND
	else
		echo ANSI_12&"**You have no cloaking devices!*"
	end
elseif ($CHOSEN_OPTION = "D")
	send "* *  t  aq  l 1*  "
	gosub :QUIKSTATS
elseif ($CHOSEN_OPTION = "G")
	send "* *  c  n  9q  q  l 1*  "
	gosub :QUIKSTATS
elseif ($CHOSEN_OPTION = "F")
	send "* * c  u  y  q  l 1*  "
	gosub :QUIKSTATS
else
	goto :WAIT_FOR_COMMAND
end
goto :WAIT_FOR_COMMAND

:DONETERRAKIT
echo #27 "[30D                           " #27 "[30D"
goto :WAIT_FOR_COMMAND

:PREFERENCESMENU
setvar $BOTISDEAF TRUE
savevar $BOTISDEAF
openmenu TWX_TOGGLEDEAF FALSE
closemenu

:REFRESHPREFERENCESMENU
killalltriggers
setarray $SPACE 27
setarray $H 27
setarray $QSS 27
setvar $H[1] "Game Stats?      "
setvar $H[2] "Ship Stats?      "
setvar $H[3] "Bot Name         "
setvar $H[4] "Login Password   "
setvar $H[5] "Bot Password     "
setvar $H[6] "Figs to drop:    "
setvar $H[7] "Limps to drop:   "
setvar $H[8] "Armids to drop:  "
setvar $H[9] "Avoid Planets?   "
setvar $H[10] "Capture after?   "
setvar $H[11] "Max Attack:      "
setvar $H[12] "Offensive Odds:  "
setvar $H[13] "Stardock     (S) "
setvar $H[14] "Rylos        (R) "
setvar $H[15] "Alpha        (A) "
setvar $H[16] "Home Sector  (H) "
setvar $H[17] "Max Fighters:    "
setvar $H[18] "Login Name:      "
setvar $H[19] "Surround type?   "
setvar $H[20] "Turn Limit:      "
setvar $H[21] "Game Letter:     "
setvar $H[22] "Safe Ship:   (X) "
setvar $H[23] "Banner Interval: "
setvar $H[24] "Alien Ships:     "
setvar $H[25] "Backdoor     (B) "
setvar $H[26] "Fig Type:        "
setvar $H[27] "                 "
if ($GAMESTATS)
	setvar $QSS[1] "Initialized"
else
	setvar $QSS[1] "No Info"
end
if ($SHIPSTATS)
	setvar $QSS[2] "Initialized"
else
	setvar $QSS[2] "No Info"
end
setvar $QSS[3] $BOT_NAME
setvar $QSS[4] $PASSWORD
setvar $QSS[5] $BOT_PASSWORD
setvar $QSS[6] $SURROUNDFIGS
setvar $QSS[7] $SURROUNDLIMP
setvar $QSS[8] $SURROUNDMINE
if ($SURROUNDAVOIDSHIELDEDONLY)
	setvar $QSS[9] "Shielded"
elseif ($SURROUNDAVOIDALLPLANETS)
	setvar $QSS[9] "All"
else
	setvar $QSS[9] "None"
end
if ($SURROUNDAUTOCAPTURE)
	setvar $QSS[10] "Yes"
else
	setvar $QSS[10] "No"
end
setvar $QSS[11] $SHIP_MAX_ATTACK
setvar $QSS[12] $SHIP_OFFENSIVE_ODDS
if ($STARDOCK > 0)
	setvar $QSS[13] $STARDOCK
else
	setvar $QSS[13] "Not Defined"
end
if ($BACKDOOR > 0)
	setvar $QSS[25] $BACKDOOR
else
	setvar $QSS[25] "Not Defined"
end
if ($RYLOS > 0)
	setvar $QSS[14] $RYLOS
else
	setvar $QSS[14] "Not Defined"
end
if ($ALPHA_CENTAURI > 0)
	setvar $QSS[15] $ALPHA_CENTAURI
else
	setvar $QSS[15] "Not Defined"
end
if ($HOME_SECTOR > 0)
	setvar $QSS[16] $HOME_SECTOR
else
	setvar $QSS[16] "Not Defined"
end
setvar $QSS[17] $SHIP_FIGHTERS_MAX
setvar $QSS[18] $USERNAME
if ($SURROUNDOVERWRITE)
	setvar $QSS[19] "All Sectors"
elseif ($SURROUNDPASSIVE)
	setvar $QSS[19] "Passive"
else
	setvar $QSS[19] "Normal"
end
if ($UNLIMITEDGAME)
	setvar $QSS[20] "Unlimited"
else
	setvar $QSS[20] $BOT_TURN_LIMIT
end
setvar $QSS[21] $LETTER
if ($SAFE_SHIP > 0)
	setvar $QSS[22] $SAFE_SHIP
else
	setvar $QSS[22] "Not Defined"
end
setvar $QSS[23] $ECHOINTERVAL&" Minutes"
if ($DROPOFFENSIVE)
	setvar $QSS[26] "Offensive"
elseif ($DROPTOLL)
	setvar $QSS[26] "Toll"
else
	setvar $QSS[26] "Defensive"
end
if ($DEFENDERCAPPING)
	setvar $QSS[24] "Using defense"
elseif ($OFFENSECAPPING)
	setvar $QSS[24] "Using offense"
else
	setvar $QSS[24] "Don't attack"
end
setvar $QSS[27] ""
setvar $QSS_TOTAL 27
gosub :MENUSPACING
echo #27&"[2J"
echo "**"
echo ANSI_11&"         General Info                     Surround/Attack Options*"
echo ANSI_10&#27&"[35m<"&#27&"[32mL"&#27&"[35m> "&ANSI_7&$QSS_VAR[18]&ANSI_10&#27&"[35m<"&#27&"[32m3"&#27&"[35m> "&ANSI_7&$QSS_VAR[6]&"*"
echo ANSI_10&#27&"[35m<"&#27&"[32mP"&#27&"[35m> "&ANSI_7&$QSS_VAR[4]&ANSI_10&#27&"[35m<"&#27&"[32m4"&#27&"[35m> "&ANSI_7&$QSS_VAR[7]&"*"
echo ANSI_10&#27&"[35m<"&#27&"[32mN"&#27&"[35m> "&ANSI_7&$QSS_VAR[3]&ANSI_10&#27&"[35m<"&#27&"[32m5"&#27&"[35m> "&ANSI_7&$QSS_VAR[8]&"*"
echo ANSI_10&#27&"[35m<"&#27&"[32mZ"&#27&"[35m> "&ANSI_7&$QSS_VAR[5]&ANSI_10&#27&"[35m<"&#27&"[32m6"&#27&"[35m> "&ANSI_7&$QSS_VAR[26]&"*"
echo ANSI_10&#27&"[35m<"&#27&"[32mG"&#27&"[35m> "&ANSI_7&$QSS_VAR[21]&ANSI_10&#27&"[35m<"&#27&"[32m7"&#27&"[35m> "&ANSI_7&$QSS_VAR[10]&"*"
echo ANSI_10&#27&"[35m<"&#27&"[32mE"&#27&"[35m> "&ANSI_7&$QSS_VAR[23]&ANSI_10&#27&"[35m<"&#27&"[32m8"&#27&"[35m> "&ANSI_7&$QSS_VAR[9]&"*"
echo ANSI_10&#27&"[35m<"&#27&"[32m1"&#27&"[35m> "&ANSI_7&$QSS_VAR[20]&ANSI_10&#27&"[35m<"&#27&"[32m9"&#27&"[35m> "&ANSI_7&$QSS_VAR[19]&"*"
echo ANSI_11&"         Capture Options                   Location Variables*"
echo ANSI_10&#27&"[35m<"&#27&"[32m2"&#27&"[35m> "&ANSI_7&$QSS_VAR[24]&ANSI_10&#27&"[35m<"&#27&"[32mS"&#27&"[35m> "&ANSI_7&$QSS_VAR[13]&"*"
echo ANSI_11&"        Current Ship Stats             "&#27&"[35m<"&#27&"[32mB"&#27&"[35m> "&ANSI_7&$QSS_VAR[25]&"*"
echo ANSI_10&"  "&ANSI_7&$QSS_VAR[12]&ANSI_10&"  "&#27&"[35m<"&#27&"[32mR"&#27&"[35m> "&ANSI_7&$QSS_VAR[14]&"*"
echo ANSI_10&"  "&ANSI_7&$QSS_VAR[11]&ANSI_10&"  "&ANSI_10&""&#27&"[35m<"&#27&"[32mA"&#27&"[35m> "&ANSI_7&$QSS_VAR[15]&"*"
echo ANSI_10&"  "&ANSI_7&$QSS_VAR[17]&ANSI_10&"  "&#27&"[35m<"&#27&"[32mH"&#27&"[35m> "&ANSI_7&$QSS_VAR[16]&"*"
echo ANSI_10&"  "&ANSI_7&$QSS_VAR[27]&ANSI_10&"  "&#27&"[35m<"&#27&"[32mX"&#27&"[35m> "&ANSI_7&$QSS_VAR[22]&"*"
echo "*"
echo ANSI_12&"           "&#27&"[35m["&#27&"[32m<"&#27&"[35m]"&ANSI_15&"Planet List                    Game Stats"&#27&"[35m["&#27&"[32m>"&#27&"[35m]*"&ANSI_7&"**"
getconsoleinput $CHOSEN_OPTION SINGLEKEY
uppercase $CHOSEN_OPTION
gosub :KILLTHETRIGGERS

:PROCESS_COMMAND
if ($CHOSEN_OPTION = "?")
	goto :REFRESHPREFERENCESMENU
elseif ($CHOSEN_OPTION = "N")
	gosub :KILLTHETRIGGERS
	getinput $NEW_BOT_NAME ANSI_13&"What is the 'in game' name of the bot? (one word, no spaces)"&ANSI_7
	striptext $NEW_BOT_NAME "^"
	striptext $NEW_BOT_NAME " "
	lowercase $NEW_BOT_NAME
	if ($NEW_BOT_NAME = "")
		goto :REFRESHPREFERENCESMENU
	end
	delete $GCONFIG_FILE
	write $GCONFIG_FILE $NEW_BOT_NAME
	setvar $BOT_NAME $NEW_BOT_NAME
elseif ($CHOSEN_OPTION = "P")
	gosub :KILLTHETRIGGERS
	getinput $PASSWORD "Please Enter your Game Password"
elseif ($CHOSEN_OPTION = "Z")
	gosub :KILLTHETRIGGERS
	getinput $BOT_PASSWORD "Please Enter your Bot Password"
elseif ($CHOSEN_OPTION = "G")
	gosub :KILLTHETRIGGERS
	getinput $LETTER "Please Enter your Game Letter"
elseif ($CHOSEN_OPTION = "L")
	gosub :KILLTHETRIGGERS
	getinput $USERNAME "Please Enter your Login Name"
elseif ($CHOSEN_OPTION = 1)
	if ($UNLIMITEDGAME = FALSE)
		gosub :KILLTHETRIGGERS
		getinput $TEMP "What are the minimum turns you need to do bot commands?"
		isnumber $TEST $TEMP
		if ($TEST)
			if (($TEMP <= 65000) and ($TEMP >= 0))
				setvar $BOT_TURN_LIMIT $TEMP
			end
		end
	end
elseif ($CHOSEN_OPTION = 3)
	gosub :KILLTHETRIGGERS
	getinput $TEMP "How many fighters to drop on surround?"
	isnumber $TEST $TEMP
	if ($TEST)
		if (($TEMP <= 50000) and ($TEMP >= 0))
			setvar $SURROUNDFIGS $TEMP
		end
	end
elseif ($CHOSEN_OPTION = 4)
	gosub :KILLTHETRIGGERS
	getinput $TEMP "How many limpets to drop on surround?"
	isnumber $TEST $TEMP
	if ($TEST)
		if (($TEMP <= 250) and ($TEMP >= 0))
			setvar $SURROUNDLIMP $TEMP
		end
	end
elseif ($CHOSEN_OPTION = 5)
	gosub :KILLTHETRIGGERS
	getinput $TEMP "How many armid mines to drop on surround?"
	isnumber $TEST $TEMP
	if ($TEST)
		if (($TEMP <= 250) and ($TEMP >= 0))
			setvar $SURROUNDMINE $TEMP
		end
	end
elseif ($CHOSEN_OPTION = 8)
	if ($SURROUNDAVOIDSHIELDEDONLY)
		setvar $SURROUNDAVOIDSHIELDEDONLY FALSE
		setvar $SURROUNDAVOIDALLPLANETS TRUE
		setvar $SURROUNDDONTAVOID FALSE
	elseif ($SURROUNDAVOIDALLPLANETS)
		setvar $SURROUNDAVOIDSHIELDEDONLY FALSE
		setvar $SURROUNDAVOIDALLPLANETS FALSE
		setvar $SURROUNDDONTAVOID TRUE
	else
		setvar $SURROUNDAVOIDSHIELDEDONLY TRUE
		setvar $SURROUNDAVOIDALLPLANETS FALSE
		setvar $SURROUNDDONTAVOID FALSE
	end
elseif ($CHOSEN_OPTION = 7)
	if ($SURROUNDAUTOCAPTURE)
		setvar $SURROUNDAUTOCAPTURE FALSE
	else
		setvar $SURROUNDAUTOCAPTURE TRUE
	end
elseif ($CHOSEN_OPTION = 2)
	if ($DEFENDERCAPPING)
		setvar $DEFENDERCAPPING FALSE
		setvar $OFFENSECAPPING TRUE
		setvar $CAPPINGALIENS TRUE
	elseif ($OFFENSECAPPING)
		setvar $DEFENDERCAPPING FALSE
		setvar $OFFENSECAPPING FALSE
		setvar $CAPPINGALIENS FALSE
	else
		setvar $DEFENDERCAPPING TRUE
		setvar $OFFENSECAPPING FALSE
		setvar $CAPPINGALIENS TRUE
	end
elseif ($CHOSEN_OPTION = 6)
	if ($DROPOFFENSIVE)
		setvar $DROPOFFENSIVE FALSE
		setvar $DROPTOLL TRUE
	elseif ($DROPTOLL)
		setvar $DROPOFFENSIVE FALSE
		setvar $DROPTOLL FALSE
	else
		setvar $DROPOFFENSIVE TRUE
		setvar $DROPTOLL FALSE
	end
elseif ($CHOSEN_OPTION = "S")
	gosub :KILLTHETRIGGERS
	getinput $TEMP "What sector is the Stardock? (0 to set to twx variable)"
	isnumber $TEST $TEMP
	if ($TEST)
		if (($TEMP <= SECTORS) and ($TEMP >= 1))
			setvar $STARDOCK $TEMP
		elseif ($TEMP = 0)
			setvar $STARDOCK STARDOCK
		end
	end
elseif ($CHOSEN_OPTION = "X")
	gosub :KILLTHETRIGGERS
	getinput $TEMP "What ship number is your safe ship?"
	isnumber $TEST $TEMP
	if ($TEST)
		setvar $SAFE_SHIP $TEMP
	end
elseif ($CHOSEN_OPTION = "E")
	gosub :KILLTHETRIGGERS
	getinput $TEMP "How many minutes afk do you want the echo banner to show each time?"
	isnumber $TEST $TEMP
	if ($TEST)
		if ($TEMP > 0)
			setvar $ECHOINTERVAL $TEMP
		else
			setvar $ECHOINTERVAL 5760
		end
	end
elseif ($CHOSEN_OPTION = "R")
	gosub :KILLTHETRIGGERS
	getinput $TEMP "What sector is the Rylos port? (0 to set to twx variable)"
	isnumber $TEST $TEMP
	if ($TEST)
		if (($TEMP <= SECTORS) and ($TEMP >= 1))
			setvar $RYLOS $TEMP
		elseif ($TEMP = 0)
			setvar $RYLOS RYLOS
		end
	end
elseif ($CHOSEN_OPTION = "A")
	gosub :KILLTHETRIGGERS
	getinput $TEMP "What sector is the Alpha Centauri port? (0 to set to twx variable)"
	isnumber $TEST $TEMP
	if ($TEST)
		if (($TEMP <= SECTORS) and ($TEMP >= 1))
			setvar $ALPHA_CENTAURI $TEMP
		elseif ($TEMP = 0)
			setvar $ALPHA_CENTAURI ALPHACENTAURI
		end
	end
elseif ($CHOSEN_OPTION = "B")
	gosub :KILLTHETRIGGERS
	getinput $TEMP "What sector is the Backdoor to Stardock?"
	isnumber $TEST $TEMP
	if ($TEST)
		if (($TEMP <= SECTORS) and ($TEMP >= 1))
			setvar $BACKDOOR $TEMP
		end
	end
elseif ($CHOSEN_OPTION = "H")
	gosub :KILLTHETRIGGERS
	getinput $TEMP "What sector is the Home Sector port?"
	isnumber $TEST $TEMP
	if ($TEST)
		if (($TEMP <= SECTORS) and ($TEMP >= 1))
			setvar $HOME_SECTOR $TEMP
		end
	end
elseif ($CHOSEN_OPTION = 9)
	if ($SURROUNDOVERWRITE)
		setvar $SURROUNDOVERWRITE FALSE
		setvar $SURROUNDPASSIVE TRUE
		setvar $SURROUNDNORMAL FALSE
	elseif ($SURROUNDPASSIVE)
		setvar $SURROUNDOVERWRITE FALSE
		setvar $SURROUNDPASSIVE FALSE
		setvar $SURROUNDNORMAL TRUE
	else
		setvar $SURROUNDOVERWRITE TRUE
		setvar $SURROUNDPASSIVE FALSE
		setvar $SURROUNDNORMAL FALSE
	end
elseif ($CHOSEN_OPTION = ">")
	goto :PREFERENCESMENUPAGE2
elseif ($CHOSEN_OPTION = "<")
	goto :PREFERENCESMENUPAGE5
else
	gosub :DONEPREFER
end
gosub :PREFERENCESTATS
goto :REFRESHPREFERENCESMENU

:DONEPREFER
openmenu TWX_TOGGLEDEAF FALSE
closemenu
echo #27 "[30D                        " #27 "[30D"
echo CURRENTANSILINE
setvar $BOTISDEAF FALSE
savevar $BOTISDEAF
goto :WAIT_FOR_COMMAND
return

:PREFERENCESTATS
savevar $ECHOINTERVAL
savevar $PASSWORD
savevar $BOT_NAME
savevar $BOT_PASSWORD
savevar $NEWPROMPT
savevar $SURROUNDAVOIDSHIELDEDONLY
savevar $SURROUNDAVOIDALLPLANETS
savevar $SURROUNDDONTAVOID
savevar $SURROUNDAUTOCAPTURE
savevar $SURROUNDFIGS
savevar $SURROUNDLIMP
savevar $SURROUNDMINE
savevar $SURROUNDOVERWRITE
savevar $SURROUNDPASSIVE
savevar $SURROUNDNORMAL
savevar $STARDOCK
savevar $BACKDOOR
savevar $RYLOS
savevar $ALPHA_CENTAURI
savevar $HOME_SECTOR
savevar $BOT_TURN_LIMIT
savevar $USERNAME
savevar $LETTER
savevar $DEFENDERCAPPING
savevar $OFFENSECAPPING
savevar $SAFE_SHIP
savevar $CAPPINGALIENS
return

:PREFERENCESMENUPAGE2
killalltriggers
setarray $SPACE 34
setarray $H 34
setarray $QSS 34
setvar $H[1] "Atomic Detonators      "
setvar $H[2] "Marker Beacons         "
setvar $H[3] "Corbomite Devices      "
setvar $H[4] "Cloaking Devices       "
setvar $H[5] "SubSpace Ether Probes  "
setvar $H[6] "Planet Scanners        "
setvar $H[7] "Limpet Tracking Mines  "
setvar $H[8] "Space Mines            "
setvar $H[9] "Photon Missiles        "
setvar $H[10] "Holographic Scan       "
setvar $H[11] "Density Scan           "
setvar $H[12] "Mine Disruptors        "
setvar $H[13] "Genesis Torpedoes      "
setvar $H[14] "TransWarp I            "
setvar $H[15] "TransWarp II           "
setvar $H[16] "Psychic Probes         "
setvar $H[17] "Limpet Removal         "
setvar $H[18] "Server Max Commands    "
setvar $H[19] "Gold Enabled           "
setvar $H[20] "MBBS Mode              "
setvar $H[21] "Multiple Photons?      "
setvar $H[22] "                       "
setvar $H[23] "Colonists Per Day      "
setvar $H[24] "Planet Trade           "
setvar $H[25] "Steal Factor           "
setvar $H[26] "Rob Factor             "
setvar $H[27] "Days To Bust Clear     "
setvar $H[28] "                       "
setvar $H[29] "Port Maximum           "
setvar $H[30] "Port Production Rate   "
setvar $H[31] "Max Port Regen Per Day "
setvar $H[32] "                       "
setvar $H[33] "Nav Haz Loss Per Day   "
setvar $H[34] "Radiation Lifetime     "
setvar $QSS[1] $ATOMIC_COST
setvar $QSS[2] $BEACON_COST
setvar $QSS[3] $CORBO_COST
setvar $QSS[4] $CLOAK_COST
setvar $QSS[5] $PROBE_COST
setvar $QSS[6] $PLANET_SCANNER_COST
setvar $QSS[7] $LIMPET_COST
setvar $QSS[8] $ARMID_COST
if ($PHOTONS_ENABLED)
	setvar $QSS[9] $PHOTON_COST
else
	setvar $QSS[9] "Disabled"
end
setvar $QSS[10] $HOLO_COST
setvar $QSS[11] $DENSITY_COST
setvar $QSS[12] $DISRUPTOR_COST
setvar $QSS[13] $GENESIS_COST
setvar $QSS[14] $TWARPI_COST
setvar $QSS[15] $TWARPII_COST
setvar $QSS[16] $PSYCHIC_COST
setvar $QSS[17] $LIMPET_REMOVAL_COST
if ($MAX_COMMANDS = 0)
	setvar $QSS[18] "Unlimited"
else
	setvar $QSS[18] $MAX_COMMANDS
end
if ($GOLDENABLED)
	setvar $QSS[19] "Yes"
else
	setvar $QSS[19] "No"
end
if ($MBBS)
	setvar $QSS[20] "Yes"
else
	setvar $QSS[20] "No"
end
if ($PHOTONS_ENABLED)
	if ($MULTIPLE_PHOTONS)
		setvar $QSS[21] "Yes"
	else
		setvar $QSS[21] "No"
	end
else
	setvar $QSS[21] "Disabled"
end
setvar $QSS[22] ""
setvar $QSS[23] $COLONIST_REGEN
setvar $QSS[24] $PTRADESETTING&"%"
setvar $QSS[25] $STEAL_FACTOR
setvar $QSS[26] $ROB_FACTOR
setvar $QSS[27] $CLEAR_BUST_DAYS
setvar $QSS[28] ""
setvar $QSS[29] $PORT_MAX
setvar $QSS[30] $PRODUCTION_RATE&"%"
setvar $QSS[31] $PRODUCTION_REGEN&"%"
setvar $QSS[32] ""
setvar $QSS[33] $DEBRIS_LOSS&"%"
setvar $QSS[34] $RADIATION_LIFETIME
setvar $QSS_TOTAL 34
gosub :MENUSPACING
echo #27&"[2J"
echo "**"
echo ANSI_11&"      Stardock Prices                 Game Statistics*"
echo ANSI_10&" "&ANSI_7&$QSS_VAR[1]&ANSI_10&""&ANSI_7&$QSS_VAR[18]&"*"
echo ANSI_10&" "&ANSI_7&$QSS_VAR[2]&ANSI_10&""&ANSI_7&$QSS_VAR[19]&"*"
echo ANSI_10&" "&ANSI_7&$QSS_VAR[3]&ANSI_10&""&ANSI_7&$QSS_VAR[20]&"*"
echo ANSI_10&" "&ANSI_7&$QSS_VAR[4]&ANSI_10&""&ANSI_7&$QSS_VAR[21]&"*"
echo ANSI_10&" "&ANSI_7&$QSS_VAR[5]&ANSI_10&""&ANSI_7&$QSS_VAR[22]&"*"
echo ANSI_10&" "&ANSI_7&$QSS_VAR[6]&ANSI_10&""&ANSI_7&$QSS_VAR[23]&"*"
echo ANSI_10&" "&ANSI_7&$QSS_VAR[7]&ANSI_10&""&ANSI_7&$QSS_VAR[24]&"*"
echo ANSI_11&" "&ANSI_7&$QSS_VAR[8]&ANSI_10&""&ANSI_7&$QSS_VAR[25]&"*"
echo ANSI_10&" "&ANSI_7&$QSS_VAR[9]&ANSI_10&""&ANSI_7&$QSS_VAR[26]&"*"
echo ANSI_10&" "&ANSI_7&$QSS_VAR[10]&ANSI_10&""&ANSI_7&$QSS_VAR[27]&"*"
echo ANSI_10&" "&ANSI_7&$QSS_VAR[11]&ANSI_10&""&ANSI_7&$QSS_VAR[28]&"*"
echo ANSI_10&" "&ANSI_7&$QSS_VAR[12]&ANSI_10&""&ANSI_7&$QSS_VAR[29]&"*"
echo ANSI_10&" "&ANSI_7&$QSS_VAR[13]&ANSI_10&""&ANSI_7&$QSS_VAR[30]&"*"
echo ANSI_10&" "&ANSI_7&$QSS_VAR[14]&ANSI_10&""&ANSI_7&$QSS_VAR[31]&"*"
echo ANSI_10&" "&ANSI_7&$QSS_VAR[15]&ANSI_10&""&ANSI_7&$QSS_VAR[32]&"*"
echo ANSI_10&" "&ANSI_7&$QSS_VAR[16]&ANSI_10&""&ANSI_7&$QSS_VAR[33]&"*"
echo ANSI_10&" "&ANSI_7&$QSS_VAR[17]&ANSI_10&""&ANSI_7&$QSS_VAR[34]&"*"
echo "*"
echo ANSI_12&"           "&#27&"[35m["&#27&"[32m<"&#27&"[35m]"&ANSI_15&"Preferences                Hot Keys"&#27&"[35m["&#27&"[32m>"&#27&"[35m]*"&ANSI_7&"**"
getconsoleinput $CHOSEN_OPTION SINGLEKEY
uppercase $CHOSEN_OPTION
gosub :KILLTHETRIGGERS
uppercase $CHOSEN_OPTION

:PROCESS_COMMANDPAGE2
if ($CHOSEN_OPTION = "?")
	goto :PREFERENCESMENUPAGE2
elseif ($CHOSEN_OPTION = ">")
	goto :PREFERENCESMENUPAGE3
elseif ($CHOSEN_OPTION = "<")
	goto :REFRESHPREFERENCESMENU
else
	gosub :DONEPREFER
end

:PREFERENCESMENUPAGE3
killalltriggers
echo #27&"[2J"
echo "**"
echo ANSI_11&"                  Custom Hotkey Definitions           *"
gosub :ECHOHOTKEYS
echo "*"
echo ANSI_12&"           "&#27&"[35m["&#27&"[32m<"&#27&"[35m]"&ANSI_15&"Game Stats                    Ship Info"&#27&"[35m["&#27&"[32m>"&#27&"[35m]*"&ANSI_7&"**"
setvar $OPTIONS "1234567890ABCDEFGHIJKLMNOPRSTUVWX"
getconsoleinput $CHOSEN_OPTION SINGLEKEY
uppercase $CHOSEN_OPTION
getwordpos $OPTIONS $POS $CHOSEN_OPTION
gosub :KILLTHETRIGGERS

:PROCESS_COMMANDPAGE3
if ($CHOSEN_OPTION = "?")
	goto :PREFERENCESMENUPAGE3
elseif ($CHOSEN_OPTION = ">")
	goto :PREFERENCESMENUPAGE4
elseif ($CHOSEN_OPTION = "<")
	goto :PREFERENCESMENUPAGE2
elseif ($POS > 0)
	gosub :KILLTHETRIGGERS
	echo "*What should this hotkey be set to?*"
	getconsoleinput $TEMP SINGLEKEY
	lowercase $TEMP
	getcharcode $TEMP $LOWER
	uppercase $TEMP
	getcharcode $TEMP $UPPER
	setvar $KEY $CUSTOM_KEYS[$POS]
	uppercase $KEY
	getcharcode $KEY $OLD_UPPER
	lowercase $KEY
	getcharcode $KEY $OLD_LOWER
	if ((((($HOTKEYS[$UPPER] = 0) or ($HOTKEYS[$UPPER] = "")) and (($HOTKEYS[$LOWER] = 0) or ($HOTKEYS[$LOWER] = "")))) and (((($LOWER < 48) or ($LOWER > 57)) and ($TEMP <> "?"))))
		setvar $HOTKEYS[$OLD_UPPER] ""
		setvar $HOTKEYS[$OLD_LOWER] ""
		setvar $HOTKEYS[$UPPER] $POS
		setvar $HOTKEYS[$LOWER] $POS
		setvar $CUSTOM_KEYS[$POS] $TEMP
		if ($POS > 17)
			getinput $TEMP "What is the bot command to connect to this hotkey?"
			setvar $CUSTOM_COMMANDS[$POS] $TEMP
		end
		setvar $I 1
		delete "__MOMBot_Hotkeys.cfg"
		delete "__MOMBot_custom_keys.cfg"
		delete "__MOMBot_custom_commands.cfg"
		while ($I <= 255)
			write "__MOMBot_hotkeys.cfg" $HOTKEYS[$I]
			add $I 1
		end
		setvar $I 1
		while ($I <= 33)
			write "__MOMBot_custom_keys.cfg" $CUSTOM_KEYS[$I]
			add $I 1
		end
		setvar $I 1
		while ($I <= 33)
			write "__MOMBot_custom_commands.cfg" $CUSTOM_COMMANDS[$I]
			add $I 1
		end
	else
		echo ANSI_4 "*Hot key already bound to another function.**" ANSI_7
		setdelaytrigger WARNINGDELAY :PREFERENCESMENUPAGE3 1000
		pause
	end
	goto :PREFERENCESMENUPAGE3
else
	gosub :DONEPREFER
end

:PREFERENCESMENUPAGE4
killalltriggers
setvar $I 1
setvar $SHIPSCHANGED FALSE
if ($SHIPCOUNTER > 10)
	setvar $PAGESEXIST TRUE
else
	setvar $PAGESEXIST FALSE
end

:NEXTSHIPPAGE
setvar $THISPAGE $I
setvar $MENUCOUNT 0
echo #27&"[2J"
echo "**"
echo ANSI_11&"                      Known Ship Information           **"
echo ANSI_15 "    Type                      Def  Off  TPW  D-Bonus?   Shields   Fighters *"
echo "   " #27 "[1m" ANSI_4 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 "*"
while (($I < $SHIPCOUNTER) and ($MENUCOUNT < 10))
	cuttext $SHIPLIST[$I]&"                                    " $TEMP 1 25
	cuttext $SHIPLIST[$I][2]&"  " $TEMPDEFHEAD 1 1
	cuttext $SHIPLIST[$I][2]&"  " $TEMPDEFTAIL 2 1
	cuttext $SHIPLIST[$I][3]&"  " $TEMPOFFHEAD 1 1
	cuttext $SHIPLIST[$I][3]&"  " $TEMPOFFTAIL 2 1
	if ($SHIPLIST[$I][8])
		setvar $TEMPDEFENDER ANSI_12&"Yes"&ANSI_14
	else
		setvar $TEMPDEFENDER "No "
	end
	cuttext $SHIPLIST[$I][1]&"              " $TEMPSHIELDS 1 10
	cuttext $SHIPLIST[$I][5]&"              " $TEMPFIGHTERS 1 6
	cuttext $SHIPLIST[$I][7]&"              " $TEMPTPW 1 3
	echo ANSI_14 "<" $MENUCOUNT "> " $TEMP " " $TEMPDEFHEAD "." $TEMPDEFTAIL "  " $TEMPOFFHEAD "." $TEMPOFFTAIL "   " $TEMPTPW "   " $TEMPDEFENDER "       " $TEMPSHIELDS " " $TEMPFIGHTERS "*"
	add $I 1
	add $MENUCOUNT 1
end
echo "   " #27 "[1m" ANSI_4 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 "*"
echo "*"
if ($PAGESEXIST)
	echo "  "&ANSI_5&"<"&ANSI_6&"+"&ANSI_5&">"&ANSI_6&" More Ships*"
end
echo "*"
echo ANSI_12&"           "&#27&"[35m["&#27&"[32m<"&#27&"[35m]"&ANSI_15&"Hot Keys                 Planet List"&#27&"[35m["&#27&"[32m>"&#27&"[35m]*"&ANSI_7&"**"
echo "  "&ANSI_5&"Toggle defender status (0-9)? "
getconsoleinput $SELECTION SINGLEKEY
setvar $OPTIONS 1234567890
uppercase $SELECTION
getwordpos $OPTIONS $POS $SELECTION
gosub :KILLTHETRIGGERS
if ($SELECTION = "<")
	gosub :REWRITE_CAP_FILE
	goto :PREFERENCESMENUPAGE3
elseif ($SELECTION = ">")
	gosub :REWRITE_CAP_FILE
	goto :PREFERENCESMENUPAGE5
elseif ($SELECTION = "?")
	gosub :REWRITE_CAP_FILE
	goto :PREFERENCESMENUPAGE4
elseif ($PAGESEXIST and ($SELECTION = "+"))
	if ($I >= $SHIPCOUNTER)
		setvar $I 1
	end
	goto :NEXTSHIPPAGE
elseif ($POS > 0)
	if ($SHIPLIST[($SELECTION + $THISPAGE)][8])
		setvar $SHIPLIST[($SELECTION + $THISPAGE)][8] FALSE
	else
		setvar $SHIPLIST[($SELECTION + $THISPAGE)][8] TRUE
	end
	setvar $I $THISPAGE
	setvar $SHIPSCHANGED TRUE
	goto :NEXTSHIPPAGE
else
	gosub :REWRITE_CAP_FILE
	gosub :DONEPREFER
end

:REWRITE_CAP_FILE
if ($SHIPSCHANGED)
	setvar $GBONUS_FILE "_MOM_"&GAMENAME&"_dbonus-ships.txt"
	delete $GBONUS_FILE
	delete $CAP_FILE
	setvar $J 1
	while ($J < $SHIPCOUNTER)
		write $CAP_FILE $SHIPLIST[$J][1]&" "&$SHIPLIST[$J][2]&" "&$SHIPLIST[$J][3]&" "&$SHIPLIST[$J][9]&" "&$SHIPLIST[$J][4]&" "&$SHIPLIST[$J][5]&" "&$SHIPLIST[$J][6]&" "&$SHIPLIST[$J][7]&" "&$SHIPLIST[$J][8]&" "&$SHIPLIST[$J]
		if ($SHIPLIST[$J][8])
			write $GBONUS_FILE $SHIPLIST[$J]
		end
		add $J 1
	end
end
return

:PREFERENCESMENUPAGE5
setvar $I 2

:NEXTPLANETPAGE
echo ANSI_12 "*Searching for enemy planets in database" ANSI_14 "...*"
killalltriggers
setvar $FOUNDSECTORS 0
setvar $DISPLAY ""
while (($I <= SECTORS) and ($FOUNDSECTORS < 3))
	getsectorparameter $I "FIGSEC" $ISFIGGED
	setvar $OWNER SECTOR.FIGS.OWNER[$I]
	if (($ISFIGGED <> TRUE) and (($OWNER <> "belong to your Corp") and ($OWNER <> "yours")))
		if (SECTOR.PLANETCOUNT[$I] > 0)
			setvar $DISPLAYSECTOR $I
			gosub :DISPLAYSECTOR
			setvar $DISPLAY $DISPLAY&"*"&$OUTPUT
			add $FOUNDSECTORS 1
		end
	end
	add $I 1
end
echo #27&"[2J"
echo "**"
echo ANSI_11&"                         Enemy Planet List*             ("&ANSI_14&"Planets in sectors not controlled by you"&ANSI_11&")              **"
echo "   " #27 "[1m" ANSI_4 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 "*"
setvar $PAGESEXIST FALSE
if ($FOUNDSECTORS > 0)
	echo $DISPLAY
	if ($I >= SECTORS)
		echo "*    [End of List]"
		setvar $I 2
	else
		setvar $PAGESEXIST TRUE
	end
else
	echo "*    [End of List]"
end
echo "*"
echo "   " #27 "[1m" ANSI_4 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 #196 "*"
echo "*"
if ($PAGESEXIST)
	echo "  "&ANSI_5&"<"&ANSI_6&"+"&ANSI_5&">"&ANSI_6&" More Planets*"
end
echo "*"
echo ANSI_12&"           "&#27&"[35m["&#27&"[32m<"&#27&"[35m]"&ANSI_15&"Ship Info                 Preferences"&#27&"[35m["&#27&"[32m>"&#27&"[35m]*"&ANSI_7&"**"
getconsoleinput $SELECTION SINGLEKEY
setvar $OPTIONS ""
uppercase $SELECTION
getwordpos $OPTIONS $POS $SELECTION
gosub :KILLTHETRIGGERS
if ($SELECTION = "<")
	goto :PREFERENCESMENUPAGE4
elseif ($SELECTION = ">")
	goto :REFRESHPREFERENCESMENU
elseif ($SELECTION = "?")
	goto :PREFERENCESMENUPAGE5
elseif ($SELECTION = "+")
	goto :NEXTPLANETPAGE
else
	gosub :DONEPREFER
end

:SECTOR

:SECTO

:SECT

:SEC
setvar $I $PARM1
isnumber $TEST $I
if ($TEST <> TRUE)
	setvar $MESSAGE "Sector entered is not a number.*"
	gosub :SWITCHBOARD
	goto :WAIT_FOR_COMMAND
end
if (($I > SECTORS) or ($I < 1))
	setvar $MESSAGE "Sector entered must be between 1 - "&SECTORS&".*"
	gosub :SWITCHBOARD
	goto :WAIT_FOR_COMMAND
end
gosub :DISPLAYSECTOR
setvar $MESSAGE $OUTPUT
if ($SELF_COMMAND <> TRUE)
	setvar $SELF_COMMAND 2
end
gosub :SWITCHBOARD
goto :WAIT_FOR_COMMAND

:DISPLAYSECTOR
setvar $OUTPUT ANSI_10&"    Sector  "&ANSI_14&": "&ANSI_11&$I&ANSI_2&" in "
setvar $CONSTELLATION SECTOR.CONSTELLATION[$I]
if ($CONSTELLATION = "The Federation.")
	setvar $OUTPUT $OUTPUT&ANSI_10&$CONSTELLATION&"*"
else
	setvar $OUTPUT $OUTPUT&ANSI_1&$CONSTELLATION&"*"
end
if (SECTOR.BEACON[$I] <> "")
	setvar $OUTPUT $OUTPUT&ANSI_5&"    Beacon  "&ANSI_14&": "&ANSI_12&SECTOR.BEACON[$I]&"*"
end
if (PORT.EXISTS[$I])
	setvar $CLASS PORT.CLASS[$I]
	setvar $OUTPUT $OUTPUT&ANSI_5&"    Ports   "&ANSI_14&": "&ANSI_11&PORT.NAME[$I]&ANSI_14&", "&ANSI_5&"Class "&ANSI_11&$CLASS&" "
	if (($CLASS <> 0) and ($CLASS <> 9))
		setvar $OUTPUT $OUTPUT&ANSI_5&"("
		if (PORT.BUYFUEL[$I])
			setvar $OUTPUT $OUTPUT&ANSI_2&"B"
		else
			setvar $OUTPUT $OUTPUT&ANSI_11&"S"
		end
		if (PORT.BUYORG[$I])
			setvar $OUTPUT $OUTPUT&ANSI_2&"B"
		else
			setvar $OUTPUT $OUTPUT&ANSI_11&"S"
		end
		if (PORT.BUYEQUIP[$I])
			setvar $OUTPUT $OUTPUT&ANSI_2&"B"
		else
			setvar $OUTPUT $OUTPUT&ANSI_11&"S"
		end
		setvar $OUTPUT $OUTPUT&ANSI_5&")"
	end
	setvar $OUTPUT $OUTPUT&"*"
end
setvar $J 1
while ($J <= SECTOR.PLANETCOUNT[$I])
	setvar $ISSHIELDED FALSE
	setvar $TEMP SECTOR.PLANETS[$I][$J]
	getword $TEMP $TEST 1
	if ($TEST = "<<<<")
		setvar $ISSHIELDED TRUE
	end
	getword $TEMP $TYPE 2
	striptext $TYPE "("
	striptext $TYPE ")"
	if ($ISSHIELDED)
		getlength $TEMP $LENGTH
		cuttext $TEMP $TEMP 1 ($LENGTH - 15)
		cuttext $TEMP $TEMP 10 9999
		setvar $TEMP ANSI_12&"<<<< "&ANSI_10&"("&ANSI_14&$TYPE&ANSI_10&") "&ANSI_1&$TEMP&ANSI_12&" >>>> "&ANSI_2&"(Shielded)"
	else
		setvar $TEMP ANSI_2&$TEMP
	end
	if ($J = 1)
		setvar $TEMP ANSI_5&"    Planets "&ANSI_14&": "&$TEMP
		setvar $OUTPUT $OUTPUT&$TEMP&"*"
	else
		setvar $OUTPUT $OUTPUT&"              "&$TEMP&"*"
	end
	add $J 1
end
if (SECTOR.FIGS.QUANTITY[$I] > 0)
	setvar $OUTPUT $OUTPUT&ANSI_5&"    Fighters"&ANSI_14&": "&ANSI_11&SECTOR.FIGS.QUANTITY[$I]&ANSI_5&" ("&SECTOR.FIGS.OWNER[$I]&") "&ANSI_6&"["&SECTOR.FIGS.TYPE[$I]&"]*"
end
setvar $OUTPUT $OUTPUT&ANSI_10&"    Warps to sector(s) "&ANSI_14&":  "
setvar $K 1
while (SECTOR.WARPS[$I][$K] > 0)
	if ($K <> 1)
		setvar $OUTPUT $OUTPUT&ANSI_2&" - "
	end
	setvar $OUTPUT $OUTPUT&ANSI_11&SECTOR.WARPS[$I][$K]
	add $K 1
end
setvar $K 1
while (SECTOR.BACKDOORS[$I][$K] > 0)
	if ($K <> 1)
		setvar $OUTPUT $OUTPUT&ANSI_2&" - "
	else
		setvar $OUTPUT $OUTPUT&ANSI_12&"*    Backdoor from sector(s) "&ANSI_14&":  "
	end
	setvar $OUTPUT $OUTPUT&ANSI_11&SECTOR.BACKDOORS[$I][$K]
	add $K 1
end
setvar $OUTPUT $OUTPUT&"**"
return

:ECHOHOTKEYS
setarray $SPACE 34
setarray $H 34
setarray $QSS 34
setvar $H[1] "Auto Kill            "
setvar $H[2] "Auto Capture         "
setvar $H[3] "Auto Refurb          "
setvar $H[4] "Surround             "
setvar $H[5] "Holo-Torp            "
setvar $H[6] "Terra Macros         "
setvar $H[7] "Planet Macros        "
setvar $H[8] "Quick Script Loading "
setvar $H[9] "Dny Holo Kill        "
setvar $H[10] "Stop Current Mode    "
setvar $H[11] "Dock Macros          "
setvar $H[12] "Exit Enter           "
setvar $H[13] "Mow                  "
setvar $H[14] "Fast Foton           "
setvar $H[15] "Clear Sector         "
setvar $H[16] "Preferences          "
setvar $H[17] "LS Dock Shopper      "
setvar $I 1
while ($I <= 16)
	if ($CUSTOM_COMMANDS[($I + 17)] <> 0)
		setvar $H[($I + 17)] $CUSTOM_COMMANDS[($I + 17)]&"                              "
		cuttext $H[($I + 17)] $H[($I + 17)] 1 22
	else
		setvar $H[($I + 17)] "Custom Hotkey "&$I&"        "
		cuttext $H[($I + 17)] $H[($I + 17)] 1 22
	end
	add $I 1
end
setvar $H[34] "                     "
setvar $I 1
while ($I <= 33)
	if (($CUSTOM_KEYS[$I] <> 0) and ($CUSTOM_KEYS[$I] <> ""))
		if ($CUSTOM_KEYS[$I] = #9)
			setvar $QSS[$I] "TAB-TAB"
		elseif ($CUSTOM_KEYS[$I] = #13)
			setvar $QSS[$I] "TAB-Enter"
		elseif ($CUSTOM_KEYS[$I] = #8)
			setvar $QSS[$I] "TAB-Backspace"
		elseif ($CUSTOM_KEYS[$I] = #32)
			setvar $QSS[$I] "TAB-Spacebar"
		else
			setvar $QSS[$I] "TAB-"&$CUSTOM_KEYS[$I]
		end
	else
		setvar $QSS[$I] "Undefined"
	end
	add $I 1
end
setvar $QSS[34] ""
setvar $QSS_TOTAL 34
gosub :MENUSPACING
echo ANSI_10&#27&"[35m<"&#27&"[32m1"&#27&"[35m> "&ANSI_7&$QSS_VAR[1]&ANSI_10&#27&"[35m<"&#27&"[32mH"&#27&"[35m> "&ANSI_7&$QSS_VAR[18]&"*"
echo ANSI_10&#27&"[35m<"&#27&"[32m2"&#27&"[35m> "&ANSI_7&$QSS_VAR[2]&ANSI_10&#27&"[35m<"&#27&"[32mI"&#27&"[35m> "&ANSI_7&$QSS_VAR[19]&"*"
echo ANSI_10&#27&"[35m<"&#27&"[32m3"&#27&"[35m> "&ANSI_7&$QSS_VAR[3]&ANSI_10&#27&"[35m<"&#27&"[32mJ"&#27&"[35m> "&ANSI_7&$QSS_VAR[20]&"*"
echo ANSI_10&#27&"[35m<"&#27&"[32m4"&#27&"[35m> "&ANSI_7&$QSS_VAR[4]&ANSI_10&#27&"[35m<"&#27&"[32mK"&#27&"[35m> "&ANSI_7&$QSS_VAR[21]&"*"
echo ANSI_10&#27&"[35m<"&#27&"[32m5"&#27&"[35m> "&ANSI_7&$QSS_VAR[5]&ANSI_10&#27&"[35m<"&#27&"[32mL"&#27&"[35m> "&ANSI_7&$QSS_VAR[22]&"*"
echo ANSI_10&#27&"[35m<"&#27&"[32m6"&#27&"[35m> "&ANSI_7&$QSS_VAR[6]&ANSI_10&#27&"[35m<"&#27&"[32mM"&#27&"[35m> "&ANSI_7&$QSS_VAR[23]&"*"
echo ANSI_10&#27&"[35m<"&#27&"[32m7"&#27&"[35m> "&ANSI_7&$QSS_VAR[7]&ANSI_10&#27&"[35m<"&#27&"[32mN"&#27&"[35m> "&ANSI_7&$QSS_VAR[24]&"*"
echo ANSI_11&#27&"[35m<"&#27&"[32m8"&#27&"[35m> "&ANSI_7&$QSS_VAR[8]&ANSI_10&#27&"[35m<"&#27&"[32mO"&#27&"[35m> "&ANSI_7&$QSS_VAR[25]&"*"
echo ANSI_10&#27&"[35m<"&#27&"[32m9"&#27&"[35m> "&ANSI_7&$QSS_VAR[9]&ANSI_10&#27&"[35m<"&#27&"[32mP"&#27&"[35m> "&ANSI_7&$QSS_VAR[26]&"*"
echo ANSI_10&#27&"[35m<"&#27&"[32m0"&#27&"[35m> "&ANSI_7&$QSS_VAR[10]&ANSI_10&#27&"[35m<"&#27&"[32mR"&#27&"[35m> "&ANSI_7&$QSS_VAR[27]&"*"
echo ANSI_10&#27&"[35m<"&#27&"[32mA"&#27&"[35m> "&ANSI_7&$QSS_VAR[11]&ANSI_10&#27&"[35m<"&#27&"[32mS"&#27&"[35m> "&ANSI_7&$QSS_VAR[28]&"*"
echo ANSI_10&#27&"[35m<"&#27&"[32mB"&#27&"[35m> "&ANSI_7&$QSS_VAR[12]&ANSI_10&#27&"[35m<"&#27&"[32mT"&#27&"[35m> "&ANSI_7&$QSS_VAR[29]&"*"
echo ANSI_10&#27&"[35m<"&#27&"[32mC"&#27&"[35m> "&ANSI_7&$QSS_VAR[13]&ANSI_10&#27&"[35m<"&#27&"[32mU"&#27&"[35m> "&ANSI_7&$QSS_VAR[30]&"*"
echo ANSI_10&#27&"[35m<"&#27&"[32mD"&#27&"[35m> "&ANSI_7&$QSS_VAR[14]&ANSI_10&#27&"[35m<"&#27&"[32mV"&#27&"[35m> "&ANSI_7&$QSS_VAR[31]&"*"
echo ANSI_10&#27&"[35m<"&#27&"[32mE"&#27&"[35m> "&ANSI_7&$QSS_VAR[15]&ANSI_10&#27&"[35m<"&#27&"[32mW"&#27&"[35m> "&ANSI_7&$QSS_VAR[32]&"*"
echo ANSI_10&#27&"[35m<"&#27&"[32mF"&#27&"[35m> "&ANSI_7&$QSS_VAR[16]&ANSI_10&#27&"[35m<"&#27&"[32mX"&#27&"[35m> "&ANSI_7&$QSS_VAR[33]&"*"
echo ANSI_10&#27&"[35m<"&#27&"[32mG"&#27&"[35m> "&ANSI_7&$QSS_VAR[17]&ANSI_10&""&ANSI_7&$QSS_VAR[34]&"*"
return

:PREGAMEMENULOAD
killalltriggers
loadvar $PASSWORD
loadvar $BOT_NAME
loadvar $STARTSHIPNAME
loadvar $MOWTODOCK
loadvar $MOWTODOCKBACKDOOR
loadvar $STARTGAMEDELAY
loadvar $ISCEO
loadvar $CORPNAME
if ($CORPNAME = 0)
	setvar $CORPNAME ""
	savevar $CORPNAME
end
loadvar $SUBSPACE
loadvar $CORPPASSWORD
if ($CORPPASSWORD = 0)
	setvar $CORPPASSWORD ""
	savevar $CORPPASSWORD
end
loadvar $USERNAME
loadvar $LETTER
if ($PASSWORD = 0)
	setvar $PASSWORD PASSWORD
end
if ($USERNAME = 0)
	setvar $USERNAME LOGINNAME
end
if ($LETTER = 0)
	setvar $LETTER GAME
end
if ($BOT_NAME = "")
	setvar $NEWGAMEDAY1 TRUE
	setvar $NEWGAMEOLDER FALSE
else
	setvar $NEWGAMEDAY1 FALSE
	setvar $NEWGAMEOLDER TRUE
end

:PREGAMEMENU
setarray $SPACE 27
setarray $H 26
setarray $QSS 26
setvar $H[1] "Bot Name:        "
setvar $H[2] "Login Name:      "
setvar $H[3] "Password:        "
setvar $H[4] "Game Letter:     "
setvar $H[5] "Ship Name:       "
setvar $H[6] "Type of login:   "
setvar $H[7] "Are you CEO?     "
setvar $H[8] "Corp Name:       "
setvar $H[9] "Corp Password:   "
setvar $H[10] "Subspace Channel:"
setvar $H[11] "Delay (Minutes): "
setvar $H[12] "After login:     "
setvar $H[13] "                 "
setvar $H[14] "                 "
setvar $H[15] "                 "
setvar $H[16] "                 "
setvar $H[17] "                 "
setvar $H[18] "                 "
setvar $H[19] "                 "
setvar $H[20] "                 "
setvar $H[21] "                 "
setvar $H[22] "                 "
setvar $H[23] "                 "
setvar $H[24] "                 "
setvar $H[25] "                 "
setvar $H[26] "                 "
setvar $QSS[1] $BOT_NAME
setvar $QSS[2] $USERNAME
setvar $QSS[3] $PASSWORD
setvar $QSS[4] $LETTER
setvar $QSS[5] $STARTSHIPNAME
if ($NEWGAMEDAY1)
	setvar $QSS[6] "New Game Account Creation"
elseif ($NEWGAMEOLDER)
	setvar $QSS[6] "Normal Relog"
else
	setvar $QSS[6] "Return after being destroyed."
end
if ($ISCEO)
	setvar $QSS[7] "Yes"
else
	setvar $QSS[7] "No"
end
setvar $QSS[8] $CORPNAME
setvar $QSS[9] $CORPPASSWORD
setvar $QSS[10] $SUBSPACE
setvar $QSS[11] $STARTGAMEDELAY
if ($MOWTODOCK)
	setvar $QSS[12] "Mow To Stardock"
elseif ($MOWTOALPHA)
	setvar $QSS[12] "Mow To Alpha"
elseif ($MOWTORYLOS)
	setvar $QSS[12] "Mow To Rylos"
elseif ($MOWTOOTHER)
	setvar $QSS[12] "Mow To Custom TA"
else
	setvar $QSS[12] "Land on Terra"
end
setvar $QSS[13] ""
setvar $QSS[14] ""
setvar $QSS[15] ""
setvar $QSS[16] ""
setvar $QSS[17] ""
setvar $QSS[18] ""
setvar $QSS[19] ""
setvar $QSS[20] ""
setvar $QSS[21] ""
setvar $QSS[22] ""
setvar $QSS[23] ""
setvar $QSS[24] ""
setvar $QSS[25] ""
setvar $QSS[26] ""
setvar $QSS_TOTAL 26
gosub :MENUSPACING
echo "**"
echo ANSI_11&" Relog Menu   (Q to quit, Z to start logging in.)         *"
echo ANSI_10&#27&"[35m<"&#27&"[32m1"&#27&"[35m> "&ANSI_7&$QSS_VAR[6]&"*"
echo "*"
echo ANSI_10&#27&"[35m<"&#27&"[32mB"&#27&"[35m> "&ANSI_7&$QSS_VAR[1]&"*"
echo ANSI_10&#27&"[35m<"&#27&"[32mL"&#27&"[35m> "&ANSI_7&$QSS_VAR[2]&"*"
echo ANSI_10&#27&"[35m<"&#27&"[32mP"&#27&"[35m> "&ANSI_7&$QSS_VAR[3]&"*"
echo ANSI_10&#27&"[35m<"&#27&"[32mG"&#27&"[35m> "&ANSI_7&$QSS_VAR[4]&"*"
if ($NEWGAMEOLDER = FALSE)
	echo ANSI_10&#27&"[35m<"&#27&"[32mS"&#27&"[35m> "&ANSI_7&$QSS_VAR[5]&"*"
end
if ($NEWGAMEDAY1 = TRUE)
	echo ANSI_10&#27&"[35m<"&#27&"[32m2"&#27&"[35m> "&ANSI_7&$QSS_VAR[7]&"*"
	echo ANSI_10&#27&"[35m<"&#27&"[32m3"&#27&"[35m> "&ANSI_7&$QSS_VAR[8]&"*"
	echo ANSI_10&#27&"[35m<"&#27&"[32m4"&#27&"[35m> "&ANSI_7&$QSS_VAR[9]&"*"
	echo ANSI_10&#27&"[35m<"&#27&"[32m5"&#27&"[35m> "&ANSI_7&$QSS_VAR[10]&"*"
end
echo ANSI_10&#27&"[35m<"&#27&"[32m6"&#27&"[35m> "&ANSI_7&$QSS_VAR[11]&"*"
if ($NEWGAMEOLDER = FALSE)
	echo ANSI_10&#27&"[35m<"&#27&"[32m7"&#27&"[35m> "&ANSI_7&$QSS_VAR[12]&"*"
end
echo "*"

:GETSTARTGAMEINPUT
getconsoleinput $CHOSEN_OPTION SINGLEKEY
gosub :KILLTHETRIGGERS
uppercase $CHOSEN_OPTION

:PROCESS_START_COMMAND
if ($CHOSEN_OPTION = "?")
	goto :PREGAMEMENU
elseif ($CHOSEN_OPTION = "B")
	gosub :KILLTHETRIGGERS
	getinput $NEW_BOT_NAME ANSI_13&"What is the 'in game' name of the bot? (one word, no spaces)"&ANSI_7
	striptext $NEW_BOT_NAME "^"
	striptext $NEW_BOT_NAME " "
	if ($NEW_BOT_NAME = "")
		goto :PREGAMEMENU
	end
	delete $GCONFIG_FILE
	write $GCONFIG_FILE $NEW_BOT_NAME
	setvar $BOT_NAME $NEW_BOT_NAME
elseif ($CHOSEN_OPTION = "P")
	gosub :KILLTHETRIGGERS
	getinput $PASSWORD "Please Enter your Game Password"
elseif ($CHOSEN_OPTION = "G")
	gosub :KILLTHETRIGGERS
	getinput $LETTER "Please Enter your Game Letter"
elseif ($CHOSEN_OPTION = "L")
	gosub :KILLTHETRIGGERS
	getinput $USERNAME "Please Enter your Login Name"
elseif ($CHOSEN_OPTION = "S")
	gosub :KILLTHETRIGGERS
	getinput $STARTSHIPNAME "What ship name would you like?"
elseif ($CHOSEN_OPTION = 1)
	if ($NEWGAMEDAY1)
		setvar $NEWGAMEDAY1 FALSE
		setvar $NEWGAMEOLDER TRUE
	elseif ($NEWGAMEOLDER)
		setvar $NEWGAMEDAY1 FALSE
		setvar $NEWGAMEOLDER FALSE
	else
		setvar $NEWGAMEDAY1 TRUE
		setvar $NEWGAMEOLDER FALSE
	end
elseif (($CHOSEN_OPTION = 2) and (($NEWGAMEDAY1 = TRUE) or ($NEWGAMEOLDER = TRUE)))
	if ($ISCEO)
		setvar $ISCEO FALSE
	else
		setvar $ISCEO TRUE
	end
elseif (($CHOSEN_OPTION = 3) and (($NEWGAMEDAY1 = TRUE) or ($NEWGAMEOLDER = TRUE)))
	getinput $TEMP "What Corp Name will you use?"
	if ($TEMP = 0)
		setvar $TEMP ""
	end
	setvar $CORPNAME $TEMP
elseif (($CHOSEN_OPTION = 4) and (($NEWGAMEDAY1 = TRUE) or ($NEWGAMEOLDER = TRUE)))
	getinput $TEMP "What Corp Password will you use?"
	if ($TEMP = 0)
		setvar $TEMP ""
	end
	setvar $CORPPASSWORD $TEMP
elseif (($CHOSEN_OPTION = 5) and (($NEWGAMEDAY1 = TRUE) or ($NEWGAMEOLDER = TRUE)))
	getinput $TEMP "What subspace channel do you want to use?"
	isnumber $TEST $TEMP
	if ($TEST)
		if (($TEMP <= 60000) and ($TEMP >= 0))
			setvar $SUBSPACE $TEMP
		end
	end
elseif ($CHOSEN_OPTION = 6)
	getinput $TEMP "How long in minutes before the game starts?"
	isnumber $TEST $TEMP
	if ($TEST)
		setvar $STARTGAMEDELAY $TEMP
	end
elseif ($CHOSEN_OPTION = 7)
	if ($MOWTODOCK)
		setvar $QSS[12] "Land on Terra"
		setvar $MOWTODOCK FALSE
		setvar $MOWTOALPHA FALSE
		setvar $MOWTORYLOS FALSE
		setvar $MOWTOOTHER FALSE
		setvar $MOWDESTINATION ""
	elseif (($MOWTOALPHA = FALSE) and (($MOWTORYLOS = FALSE) and (($MOWTOOTHER = FALSE) and ($MOWTODOCK = FALSE))))
		setvar $QSS[12] "Mow To Alpha"
		setvar $MOWTODOCK FALSE
		setvar $MOWTOALPHA TRUE
		setvar $MOWTORYLOS FALSE
		setvar $MOWTOOTHER FALSE
		setvar $MOWDESTINATION $ALPHA_CENTAURI
	elseif ($MOWTOALPHA)
		setvar $QSS[12] "Mow To Rylos"
		setvar $MOWTODOCK FALSE
		setvar $MOWTOALPHA FALSE
		setvar $MOWTORYLOS TRUE
		setvar $MOWTOOTHER FALSE
		setvar $MOWDESTINATION $RYLOS
	elseif ($MOWTORYLOS)
		setvar $QSS[12] "Mow To Custom TA"
		setvar $MOWTODOCK FALSE
		setvar $MOWTOALPHA FALSE
		setvar $MOWTORYLOS FALSE
		setvar $MOWTOOTHER TRUE
		setvar $MOWDESTINATION ""
	elseif ($MOWTOOTHER)
		setvar $QSS[12] "Mow to Stardock"
		setvar $MOWTODOCK TRUE
		setvar $MOWTOALPHA FALSE
		setvar $MOWTORYLOS FALSE
		setvar $MOWTOOTHER FALSE
		setvar $MOWDESTINATION $STARDOCK
	end
elseif ($CHOSEN_OPTION = "Q")
	stop $LAST_LOADED_MODULE
	setvar $DORELOG FALSE
	goto :WAIT_FOR_COMMAND
elseif ($CHOSEN_OPTION = "Z")

:GETMOWSECTOR
	killalltriggers
	if ($MOWTOOTHER)
		getinput $TEMP "What mow destination do you want to use?"
		isnumber $TEST $TEMP
		if ($TEST)
			if (($TEMP <= SECTORS) and ($TEMP > 0))
				setvar $MOWDESTINATION $TEMP
			else
				goto :GETMOWSECTOR
			end
		else
			goto :GETMOWSECTOR
		end
	end
	setvar $TIMETOLOGBACKIN ($STARTGAMEDELAY * 60)
	settextouttrigger LOGEARLY :ENDDELAYSTARTGAME #32
	while ($TIMETOLOGBACKIN > 0)
		gosub :CALCTIME
		echo ANSI_10 #27&"[1A"&#27&"[K"&$HOURS ":" $MINUTES ":" $SECONDS " left before entering game " GAME " (" GAMENAME ") "&ANSI_15&" ["&ANSI_14&"Spacebar to relog"&ANSI_15&"]*"
		setdelaytrigger TIMEBEFORERELOG :STARTGAMETIMER 1000
		pause

		:STARTGAMETIMER
		setvar $TIMETOLOGBACKIN ($TIMETOLOGBACKIN - 1)
	end

	:ENDDELAYSTARTGAME
	killalltriggers
	if ($NEWGAMEOLDER = TRUE)
		goto :RELOG_ATTEMPT
	elseif ($NEWGAMEDAY1 = TRUE)

	:TRYAGAINNEWGAMEDAY1
		gosub :DO_RELOG
		settextlinetrigger GAMECLOSED1 :GAMECLOSED "I'm sorry, but this is a closed game."
		settextlinetrigger GAMECLOSED2 :GAMECLOSED "www.tradewars.com                                   Epic Interactive Strategy"
		settextlinetrigger DAMN_PLANET :DAMN_PLANET "What do you want to name your home planet?"
		settexttrigger PHEW :PHEW "Command [TL"
		send "T***Y"&$PASSWORD&"*"&$PASSWORD&"**N"&$USERNAME&"*Y"&$STARTSHIPNAME&"*Y"
		pause

		:GAMECLOSED
		killalltriggers
		disconnect
		setdelaytrigger WHISTLEWHILEYOUWORK :WHISTLEWHILEYOUWORK 1000
		pause

		:WHISTLEWHILEYOUWORK
		goto :TRYAGAINNEWGAMEDAY1

		:DAMN_PLANET
		send ".*  Q  "
		pause

		:PHEW
		killalltriggers
		if (($ISCEO = TRUE) and (($CORPNAME <> "") and ($CORPPASSWORD <> "")))
			settextlinetrigger ALREADYCORPED :ALREADYCORPED "You may only be on one Corp at a time."
			settexttrigger CONTINUECORPCREATION :CONTINUECORPCREATION "<Create New Corporation>"
			send "*TM"
			waitfor "Enter Corp name"
			pause

			:CONTINUECORPCREATION
			killalltriggers
			send $CORPNAME&"*Y"&$CORPPASSWORD&"*Y*CN24"&$SUBSPACE&"* Q Q Q ZN* ^Q"
		elseif (($ISCEO = FALSE) and (($CORPNAME <> "") and ($CORPPASSWORD <> "")))

		:CHECKFORCORP
			send "*TD"
			gosub :QUIKSTATS
			settextlinetrigger THEREISMYCORP :THEREISMYCORP "    "&$CORPNAME
			settexttrigger NOCORPTHATNAME :NOCORPTHATNAME "Corporate command ["
			send "L"
			pause

			:NOCORPTHATNAME
			killalltriggers
			echo "Waiting 5 seconds to check for corp again, press [Spacebar] to cancel.*"
			setdelaytrigger WAITINGFORCORP :CHECKFORCORP 5000
			settextouttrigger CANCELWAITINGFORCORP :ALREADYCORPED #32
			pause

			:THEREISMYCORP
			killalltriggers
			getword CURRENTLINE $CORPNUMBER 1

			:CONTINUECORPCREATION
			killalltriggers
			send "J"&$CORPNUMBER&"*"&$CORPPASSWORD&"* * *CN24"&$SUBSPACE&"* Q Q Q ZN* ^Q"
		else

			:ALREADYCORPED
			killalltriggers
			send "* * *CN24"&$SUBSPACE&"*Q Q Q ZN* ^Q"
		end
		settextlinetrigger ALLDONE :ALLDONE ": ENDINTERROG"
		pause

		:ALLDONE
		killalltriggers
	else

		:TRYAGAINSD
		gosub :DO_RELOG
		settextlinetrigger GAMECLOSED1 :GAMECLOSEDSD "I'm sorry, but this is a closed game."
		settextlinetrigger GAMECLOSED2 :GAMECLOSEDSD "www.tradewars.com                                   Epic Interactive Strategy"
		settextlinetrigger DAMN_PLANET :DAMN_PLANETSD "What do you want to name your home planet?"
		settexttrigger PHEW :PHEWSD "Command [TL"
		send "T***"&$PASSWORD&"*         * *"&$STARTSHIPNAME&"*Y "
		pause

		:GAMECLOSEDSD
		killalltriggers
		disconnect
		setdelaytrigger WHISTLEWHILEYOUWORKSD :WHISTLEWHILEYOUWORKSD 1000
		pause

		:WHISTLEWHILEYOUWORKSD
		goto :TRYAGAINSD

		:DAMN_PLANETSD
		send ".*  Q  "
		pause

		:PHEWSD
		killalltriggers
	end
	goto :DONEPREGAME
else
	goto :GETSTARTGAMEINPUT
end
gosub :PREGAMESTATS
goto :PREGAMEMENU

:DONEPREGAME
echo #27 "[30D                        " #27 "[30D"
if ($MOWTODOCK or $MOWTORYLOS or $MOWTOALPHA or $MOWTOOTHER)
	if ((STARDOCK = 0) or (STARDOCK = ""))
		send "v"
		waiton "-=-=-=-  Current "
	end
	if ((STARDOCK = 0) or (STARDOCK = ""))
		send "'{" $BOT_NAME "} - Stardock appears to be hidden in this game. Aborting mow.*"
	else
		setvar $USER_COMMAND_LINE "mow "&$MOWDESTINATION&" 1 p"
		setvar $PARM1 $MOWDESTINATION
		setvar $PARM2 1
		gosub :DO_MOW
	end
else
	settexttrigger LANDED :LANDED_ON_TERRA "Do you wish to (L)eave or (T)ake Colonists?"
	setdelaytrigger LANDING_TIMEOUT :LANDING_TIMEOUT 5000
	send "l "
	pause

	:LANDING_TIMEOUT
	send "'{" $BOT_NAME "} - Could not land on Terra!  Probably not in sector 1.*"
	goto :DONE_LANDING_TERRA

	:LANDED_ON_TERRA
	send "'{" $BOT_NAME "} - Safely on Terra.*"

	:DONE_LANDING_TERRA
end
goto :GETINITIAL_SETTINGS
return

:PREGAMESTATS
savevar $PASSWORD
savevar $BOT_NAME
savevar $STARTSHIPNAME
savevar $MOWTODOCK
savevar $MOWTODOCKBACKDOOR
savevar $STARTGAMEDELAY
savevar $ISCEO
savevar $CORPNAME
savevar $SUBSPACE
savevar $CORPPASSWORD
savevar $USERNAME
savevar $LETTER
savevar $NEWGAMEDAY1
savevar $NEWGAMEOLDER
return

:MENUSPACING
setarray $QSS_VAR 100
setvar $QSS_SS 0
setvar $QSS_COUNT 1
setvar $SPC " "
setvar $OVERALL 15
while ($QSS_COUNT <= $QSS_TOTAL)
	setvar $SPC_COUNT 1
	setvar $CHECKLENGTH $H[$QSS_COUNT]&""&$QSS[$QSS_COUNT]
	setvar $QSS_VAR[$QSS_COUNT] ANSI_15&$H[$QSS_COUNT]&" "&ANSI_14&$QSS[$QSS_COUNT]&ANSI_7
	getlength $CHECKLENGTH $LENGTH
	setvar $SPACE 34
	subtract $SPACE $LENGTH
	while ($SPC_COUNT <= $SPACE)
		mergetext $QSS_VAR[$QSS_COUNT] $SPC $QSS_VAR[$QSS_COUNT]
		add $SPC_COUNT 1
	end
	add $QSS_COUNT 1
end
return

:DOSPLASHSCREEN
setdelaytrigger DRAW_DELAY_01298A :DRAW_DELAY_01298A 300
pause

:DRAW_DELAY_01298A
send "'*M()MBot " $MAJOR_VERSION "." $MINOR_VERSION "*"
send " *"
send "Original Authors*"
send "            - Mind Dagger / The Bounty Hunter / Lonestar*"
send "Credits Go Out To*"
send "            - Oz, Zentock, SupG, Dynarri, Cherokee, Alexio, Xide*"
send "            - Phx, Rincrast, Voltron, Traitor, Parrothead*"
send "Current Version Modified for TWGS 1.03 & 2.19+*"
send "            - T0yman, Xanos**"
setvar $_SPLASH_ " "
return

:LOAD_BOT
setvar $MAJOR_VERSION 3
setvar $MINOR_VERSION 1045
gosub :DOSPLASHSCREEN
fileexists $EXISTS1 "__MOMBot_Hotkeys.cfg"
fileexists $EXISTS2 "__MOMBot_custom_keys.cfg"
fileexists $EXISTS3 "__MOMBot_custom_commands.cfg"
if ($EXISTS1 and ($EXISTS2 and $EXISTS3))
	readtoarray "__MOMBot_Hotkeys.cfg" $HOTKEYS
	readtoarray "__MOMBot_custom_keys.cfg" $CUSTOM_KEYS
	readtoarray "__MOMBot_custom_commands.cfg" $CUSTOM_COMMANDS
end
if (($EXISTS1 = FALSE) or ($EXISTS2 = FALSE) or ($EXISTS3 = FALSE) or ($HOTKEYS <> 255) or ($CUSTOM_KEYS <> 33) or ($CUSTOM_COMMANDS <> 33))
	delete "__MOMBot_Hotkeys.cfg"
	delete "__MOMBot_custom_keys.cfg"
	delete "__MOMBot_custom_commands.cfg"
	setarray $HOTKEYS 255
	setarray $CUSTOM_KEYS 33
	setarray $CUSTOM_COMMANDS 33
	setvar $HOTKEYS[76] 9
	setvar $HOTKEYS[108] 9
	setvar $HOTKEYS[102] 14
	setvar $HOTKEYS[70] 14
	setvar $HOTKEYS[109] 13
	setvar $HOTKEYS[77] 13
	setvar $HOTKEYS[104] 5
	setvar $HOTKEYS[72] 5
	setvar $HOTKEYS[107] 1
	setvar $HOTKEYS[75] 1
	setvar $HOTKEYS[99] 2
	setvar $HOTKEYS[67] 2
	setvar $HOTKEYS[98] 17
	setvar $HOTKEYS[66] 17
	setvar $HOTKEYS[112] 7
	setvar $HOTKEYS[80] 7
	setvar $HOTKEYS[100] 11
	setvar $HOTKEYS[68] 11
	setvar $HOTKEYS[116] 6
	setvar $HOTKEYS[84] 6
	setvar $HOTKEYS[114] 3
	setvar $HOTKEYS[82] 3
	setvar $HOTKEYS[115] 4
	setvar $HOTKEYS[83] 4
	setvar $HOTKEYS[120] 12
	setvar $HOTKEYS[88] 12
	setvar $HOTKEYS[122] 15
	setvar $HOTKEYS[90] 15
	setvar $HOTKEYS[126] 16
	setvar $HOTKEYS[113] 8
	setvar $HOTKEYS[81] 8
	setvar $HOTKEYS[9] 10
	setvar $CUSTOM_KEYS[1] "K"
	setvar $CUSTOM_KEYS[2] "C"
	setvar $CUSTOM_KEYS[3] "R"
	setvar $CUSTOM_KEYS[4] "S"
	setvar $CUSTOM_KEYS[5] "H"
	setvar $CUSTOM_KEYS[6] "T"
	setvar $CUSTOM_KEYS[7] "P"
	setvar $CUSTOM_KEYS[8] "Q"
	setvar $CUSTOM_KEYS[9] "L"
	setvar $CUSTOM_KEYS[10] #9
	setvar $CUSTOM_KEYS[11] "D"
	setvar $CUSTOM_KEYS[12] "X"
	setvar $CUSTOM_KEYS[13] "M"
	setvar $CUSTOM_KEYS[14] "F"
	setvar $CUSTOM_KEYS[15] "Z"
	setvar $CUSTOM_KEYS[16] "~"
	setvar $CUSTOM_KEYS[17] "B"
	setvar $CUSTOM_COMMANDS[1] ":autokill"
	setvar $CUSTOM_COMMANDS[2] ":autocap"
	setvar $CUSTOM_COMMANDS[3] ":autorefurb"
	setvar $CUSTOM_COMMANDS[4] ":surround"
	setvar $CUSTOM_COMMANDS[5] "htorp"
	setvar $CUSTOM_COMMANDS[6] ":terraKit"
	setvar $CUSTOM_COMMANDS[7] ":psiMacs"
	setvar $CUSTOM_COMMANDS[8] ":script_access"
	setvar $CUSTOM_COMMANDS[9] "hkill"
	setvar $CUSTOM_COMMANDS[10] ":stopModules"
	setvar $CUSTOM_COMMANDS[11] ":dockKit"
	setvar $CUSTOM_COMMANDS[12] "xenter"
	setvar $CUSTOM_COMMANDS[13] ":mowswitch"
	setvar $CUSTOM_COMMANDS[14] ":fotonswitch"
	setvar $CUSTOM_COMMANDS[15] "clear"
	setvar $CUSTOM_COMMANDS[16] ":preferencesMenu"
	setvar $CUSTOM_COMMANDS[17] ":dock_shopper"
end
setvar $STARTINGLOCATION ""
setarray $INTERNALCOMMANDLISTS 7
setvar $INTERNALCOMMANDLISTS[1] " holo dscan stopall listall reset emq bot relog tow mac refresh login logoff xport max topoff nmac unlock land lift twarp bwarp pwarp with dep callin about cn"
setvar $INTERNALCOMMANDLISTS[2] " qset "
setvar $INTERNALCOMMANDLISTS[3] " pe pxex pxe pxed pxel pel pelk pxelk pex ped htorp hkill kill cap "
setvar $INTERNALCOMMANDLISTS[4] " max refurb scrub "
setvar $INTERNALCOMMANDLISTS[5] " surround safemow mow pgrid exit plimp limp climp pmine cmine mine mines clear "
setvar $INTERNALCOMMANDLISTS[6] " rob mega max clearbusts"
setvar $INTERNALCOMMANDLISTS[7] " course qss status overload find bustcount holo dscan pscan disp sector figs limps armids ping plist slist storeship setvar getvar "
setvar $DOUBLEDCOMMANDLIST " sec sect secto cn9 maxport logout emx smow q l m t b port x shipstore w d finder f nf fp p de uf nfup fup fde xenter status countbusts countbust "
setvar $INTERNALCOMMANDLIST $INTERNALCOMMANDLISTS[1]&$INTERNALCOMMANDLISTS[2]&$INTERNALCOMMANDLISTS[3]&$INTERNALCOMMANDLISTS[4]&$INTERNALCOMMANDLISTS[5]&$INTERNALCOMMANDLISTS[6]&$INTERNALCOMMANDLISTS[7]
setarray $TYPES 7
setvar $TYPES[1] "General"
setvar $TYPES[2] "Defense"
setvar $TYPES[3] "Offense"
setvar $TYPES[4] "Resource"
setvar $TYPES[5] "Grid"
setvar $TYPES[6] "Cashing"
setvar $TYPES[7] "Data"
setarray $CATAGORIES 3
setvar $CATAGORIES[1] "Modes"
setvar $CATAGORIES[2] "Commands"
setvar $CATAGORIES[3] "Daemons"
setvar $AUTHORIZED_FILE "_MOM_"&GAMENAME&".authorized"
fileexists $AUTHFILE $AUTHORIZED_FILE
if ($AUTHFILE = FALSE)
	setvar $CORPYCOUNT 0
else
	readtoarray $AUTHORIZED_FILE $AUTHLIST
	setvar $I 1
	while ($I <= $AUTHLIST)
		if ($AUTHLIST <> "")
			add $CORPYCOUNT 1
			setvar $CORPY[$CORPYCOUNT] $AUTHLIST[$I]
			echo $AUTHLIST[$I]&" "
		end
		add $I 1
	end
	echo "**"
end
setarray $CORPY 100
setvar $SHIPSTATS FALSE
setvar $GAMESTATS FALSE
setvar $SCRIPT_NAME "Mind ()ver Matter Bot "
setvar $WARN "OFF"
setvar $MODE "General"
setvar $SELF_COMMAND FALSE
setvar $OKAYTOUSE TRUE
setvar $TRADER_NAME ""
setarray $PARMS 8
setvar $PARMS 8
setvar $MODULECATEGORY ""
setvar $START_FIG_HIT "Deployed Fighters Report Sector "
setvar $END_FIG_HIT ":"
setvar $ALIEN_ANSI #27&"[1;36m"&#27&"["
setvar $START_FIG_HIT_OWNER ":"
setvar $END_FIG_HIT_OWNER "'s"
setvar $GCONFIG_FILE "_MOM_"&GAMENAME&".bot"
setvar $CK_FIG_FILE "_ck_"&GAMENAME&".figs"
setvar $FIG_FILE "_MOM_"&GAMENAME&".figs"
setvar $FIG_COUNT_FILE "_MOM_"&GAMENAME&"_Fighter_Count.cnt"
savevar $CK_FIG_FILE
savevar $FIG_FILE
savevar $FIG_COUNT_FILE
setvar $LIMP_FILE "_MOM_"&GAMENAME&".limps"
setvar $LIMP_COUNT_FILE "_MOM_"&GAMENAME&"_Limpet_Count.cnt"
savevar $LIMP_FILE
savevar $LIMP_COUNT_FILE
setvar $ARMID_COUNT_FILE "_MOM_"&GAMENAME&"_Armid_Count.cnt"
setvar $ARMID_FILE "_MOM_"&GAMENAME&".armids"
savevar $ARMID_COUNT_FILE
savevar $ARMID_FILE
setvar $GAME_SETTINGS_FILE "_MOM_"&GAMENAME&"_Game_Settings.cfg"
setvar $BOT_USER_FILE "_MOM_"&GAMENAME&"_Bot_Users.lst"
setvar $CAP_FILE "_MOM_"&GAMENAME&".ships"
setvar $SCRIPT_FILE "_MOM_HOTKEYSCRIPTS.txt"
setvar $BUST_FILE "_MOM_"&GAMENAME&"Busts.txt"
setvar $BOT_FILE "scripts\__mom_bot"&$MAJOR_VERSION&"_"&$MINOR_VERSION
setvar $MCIC_FILE GAMENAME&".nego"
fileexists $TEST $BOT_FILE
if ($TEST)
	setvar $BOT_FILE "scripts\__mom_bot"&$MAJOR_VERSION&"_"&$MINOR_VERSION
else
	setvar $BOT_FILE "scripts\MomBot\__mom_bot"&$MAJOR_VERSION&"_"&$MINOR_VERSION
end
setvar $LAST_LOADED_MODULE ""
setarray $HISTORY 100
setvar $PROMPTOUTPUT ""
setvar $CHARCOUNT 0
setvar $HISTORYINDEX 0
setvar $CURRENTPROMPTTEXT ""
setvar $HISTORYMAX 100
setvar $HISTORYCOUNT 0
setvar $CHARPOS 0
setvar $OVERHAGGLEMULTIPLE 147
setvar $CYCLEBUFFER 1
setvar $CYCLEBUFFERLIMIT 20
setvar $PORT_PRODUCTION_MAX 0
setvar $PLAYER_CASH_MAX 999999999
setvar $CITADEL_CASH_MAX "999999999999999"
setvar $CURRENT_PROMPT "Undefined"
setvar $PSYCHIC_PROBE "No"
setvar $PLANET_SCANNER "No"
setvar $SCAN_TYPE "None"
setarray $TRADERS 200
setarray $FAKETRADERS 200
setarray $SHIPLIST 200
setarray $THESHIPS 2000
setvar $RANKSLENGTH 47
setarray $RANKS $RANKSLENGTH
setvar $RANKS[1] "36mCivilian"
setvar $RANKS[2] "36mPrivate 1st Class"
setvar $RANKS[3] "36mPrivate"
setvar $RANKS[4] "36mLance Corporal"
setvar $RANKS[5] "36mCorporal"
setvar $RANKS[6] "36mStaff Sergeant"
setvar $RANKS[7] "36mGunnery Sergeant"
setvar $RANKS[8] "36m1st Sergeant"
setvar $RANKS[9] "36mSergeant Major"
setvar $RANKS[10] "36mSergeant"
setvar $RANKS[11] "31mAnnoyance"
setvar $RANKS[12] "31mNuisance 3rd Class"
setvar $RANKS[13] "31mNuisance 2nd Class"
setvar $RANKS[14] "31mNuisance 1st Class"
setvar $RANKS[15] "31mMenace 3rd Class"
setvar $RANKS[16] "31mMenace 2nd Class"
setvar $RANKS[17] "31mMenace 1st Class"
setvar $RANKS[18] "31mSmuggler 3rd Class"
setvar $RANKS[19] "31mSmuggler 2nd Class"
setvar $RANKS[20] "31mSmuggler 1st Class"
setvar $RANKS[21] "31mSmuggler Savant"
setvar $RANKS[22] "31mRobber"
setvar $RANKS[23] "31mTerrorist"
setvar $RANKS[24] "31mInfamous Pirate"
setvar $RANKS[25] "31mNotorious Pirate"
setvar $RANKS[26] "31mDread Pirate"
setvar $RANKS[27] "31mPirate"
setvar $RANKS[28] "31mGalactic Scourge"
setvar $RANKS[29] "31mEnemy of the State"
setvar $RANKS[30] "31mEnemy of the People"
setvar $RANKS[31] "31mEnemy of Humankind"
setvar $RANKS[32] "31mHeinous Overlord"
setvar $RANKS[33] "31mPrime Evil"
setvar $RANKS[34] "36mChief Warrant Officer"
setvar $RANKS[35] "36mWarrant Officer"
setvar $RANKS[36] "36mEnsign"
setvar $RANKS[37] "36mLieutenant J.G."
setvar $RANKS[38] "36mLieutenant Commander"
setvar $RANKS[39] "36mLieutenant"
setvar $RANKS[40] "36mCommander"
setvar $RANKS[41] "36mCaptain"
setvar $RANKS[42] "36mCommodore"
setvar $RANKS[43] "36mRear Admiral"
setvar $RANKS[44] "36mVice Admiral"
setvar $RANKS[45] "36mFleet Admiral"
setvar $RANKS[46] "36mAdmiral"
setvar $ENDLINE "_ENDLINE_"
setvar $STARTLINE "_STARTLINE_"
setvar $LASTTARGET ""
setvar $LSD_CURENT_VERSION "4.0"
setvar $LSD_TAGLINEB "LSDv"&$LSD_CURENT_VERSION
setvar $LSD_SHIPDATA_VALID FALSE
setvar $LSD_SHIPS_NAMES "][LSD]["
setvar $LSD_SHIPS_FILE "LSD_"&GAMENAME&".ships"
setvar $LSD_SHIPLISTMAX 50
setvar $LSD_BOTTING $BOT_NAME
setvar $LSD__PAD "@"
setarray $LSD_SHIPLIST $LSD_SHIPLISTMAX 3

:GETINITIAL_SETTINGS
gosub :VALIDATION
loadvar $GAMESTATS
setvar $PGRID_TYPE "Normal"
setvar $PGRID_END_COMMAND " scan "
getword CURRENTLINE $STARTINGLOCATION 1
fileexists $SCRIPT_FILE_CHK $SCRIPT_FILE
if ($SCRIPT_FILE_CHK)
	setarray $HOTKEY_SCRIPTS 10 1
	setvar $I 1
	setvar $HOTKEY_SCRIPTS 0
	read $SCRIPT_FILE $LINE $I
	while ($LINE <> EOF)
		getword $LINE $FILELOCATION 1
		getwordpos $LINE $POS #34
		if ($POS <= 0)
			echo "Error with script file. either remove "&$SCRIPT_FILE&", or fix it*"
			halt
		end
		cuttext $LINE $SCRIPTNAME $POS 9999
		striptext $SCRIPTNAME #34
		setvar $HOTKEY_SCRIPTS[$I] $FILELOCATION
		setvar $HOTKEY_SCRIPTS[$I][1] $SCRIPTNAME
		add $I 1
		add $HOTKEY_SCRIPTS 1
		read $SCRIPT_FILE $LINE $I
	end
else
	setarray $HOTKEY_SCRIPTS 10 2
end
fileexists $GFILE_CHK $GCONFIG_FILE
if ($GFILE_CHK)
	loadvar $MBBS
	loadvar $STEAL_FACTOR
	loadvar $ROB_FACTOR
	loadvar $PTRADESETTING
	loadvar $PORT_MAX
	loadvar $UNLIMITEDGAME
	setvar $DORELOG TRUE
	savevar $DORELOG
	read $GCONFIG_FILE $BOT_NAME 1
	if ((($STARTINGLOCATION = "Command") or ($STARTINGLOCATION = "Citadel")) and (CONNECTED = TRUE))
		gosub :QUIKSTATS
		if ($GAMESTATS = FALSE)
			gosub :GAMESTATS
		end
		gosub :GETINFO
		gosub :GETSHIPSTATS
		fileexists $CAP_FILE_CHK $CAP_FILE
		if ($CAP_FILE_CHK)
			gosub :LOADSHIPINFO
		else
			gosub :GETSHIPCAPSTATS
			gosub :LOADSHIPINFO
		end
	else
		fileexists $CAP_FILE_CHK $CAP_FILE
		if ($CAP_FILE_CHK)
			gosub :LOADSHIPINFO
		end
	end
else

	:CONF_BOT
	echo "*{M()M-Bot} . . . Getting Initial Settings . . . "
	echo "*{M()M-Bot} . . . Communications Off . . . **"
	echo ANSI_13 "*Game is not set up for M()M-Bot, doing now . . . *"
	setvar $NEWPROMPT TRUE
	setvar $SURROUNDFIGS 1
	savevar $SURROUNDFIGS
	savevar $NEWPROMPT
	gosub :ADD_GAME
	if ((($STARTINGLOCATION = "Command") or ($STARTINGLOCATION = "Citadel")) and (CONNECTED = TRUE))
		gosub :GAMESTATS
		gosub :QUIKSTATS
		gosub :GETINFO
		fileexists $CAP_FILE_CHK $CAP_FILE
		if ($CAP_FILE_CHK)
			gosub :LOADSHIPINFO
		else
			gosub :GETSHIPCAPSTATS
			gosub :LOADSHIPINFO
		end
	else
		fileexists $CAP_FILE_CHK $CAP_FILE
		if ($CAP_FILE_CHK)
			gosub :LOADSHIPINFO
		end
	end
end
getsectorparameter 2 "FIG_COUNT" $FIGCOUNT
if ($FIGCOUNT = "")
	setsectorparameter 2 "FIG_COUNT" 0
end
loadvar $ECHOINTERVAL
if ($ECHOINTERVAL <= 0)
	setvar $ECHOINTERVAL 60
	savevar $ECHOINTERVAL
end
setvar $BOTISOFF FALSE
loadvar $OFFENSECAPPING
loadvar $CAPPINGALIENS
loadvar $PLANET
loadvar $ATOMIC_COST
loadvar $BEACON_COST
loadvar $CORBO_COST
loadvar $CLOAK_COST
loadvar $PROBE_COST
loadvar $PLANET_SCANNER_COST
loadvar $LIMPET_COST
loadvar $ARMID_COST
loadvar $PHOTON_COST
loadvar $HOLO_COST
loadvar $DENSITY_COST
loadvar $DISRUPTOR_COST
loadvar $GENESIS_COST
loadvar $TWARPI_COST
loadvar $TWARPII_COST
loadvar $PSYCHIC_COST
loadvar $PHOTONS_ENABLED
loadvar $PHOTON_DURATION
loadvar $MAX_COMMANDS
loadvar $GOLDENABLED
loadvar $MBBS
loadvar $MULTIPLE_PHOTONS
loadvar $COLONIST_REGEN
loadvar $PTRADESETTING
loadvar $STEAL_FACTOR
loadvar $ROB_FACTOR
loadvar $CLEAR_BUST_DAYS
loadvar $PORT_MAX
loadvar $PRODUCTION_RATE
loadvar $PRODUCTION_REGEN
loadvar $DEBRIS_LOSS
loadvar $RADIATION_LIFETIME
loadvar $LIMPET_REMOVAL_COST
loadvar $MAX_PLANETS_PER_SECTOR
loadvar $SUBSPACE
loadvar $PASSWORD
loadvar $BOT_PASSWORD
loadvar $NEWPROMPT
loadvar $SURROUNDAVOIDSHIELDEDONLY
loadvar $SURROUNDAUTOCAPTURE
loadvar $SURROUNDAVOIDALLPLANETS
loadvar $SURROUNDDONTAVOID
loadvar $STARDOCK
loadvar $BACKDOOR
loadvar $RYLOS
loadvar $ALPHA_CENTAURI
loadvar $HOME_SECTOR
loadvar $SURROUNDFIGS
loadvar $SURROUNDLIMP
loadvar $SURROUNDMINE
loadvar $SURROUNDOVERWRITE
loadvar $SURROUNDPASSIVE
loadvar $SURROUNDNORMAL
loadvar $USERNAME
loadvar $LETTER
loadvar $DEFENDERCAPPING
loadvar $BOT_TURN_LIMIT
loadvar $SAFE_SHIP
loadvar $BOT_TEAM_NAME
loadvar $HISTORYSTRING
loadvar $DORELOG
setvar $HISTORYCOUNT 0
getwordpos $HISTORYSTRING $POS "<<|HS|>>"
while (($POS > 0) and ($HISTORYCOUNT < $HISTORYMAX))
	cuttext $HISTORYSTRING $ARCHIVE 1 ($POS - 1)
	replacetext $HISTORYSTRING $ARCHIVE&"<<|HS|>>" ""
	setvar $HISTORY[($HISTORYCOUNT + 1)] $ARCHIVE
	add $HISTORYCOUNT 1
	getwordpos $HISTORYSTRING $POS "<<|HS|>>"
end
if (($SURROUNDAVOIDSHIELDEDONLY = FALSE) and (($SURROUNDAUTOCAPTURE = FALSE) and (($SURROUNDAVOIDALLPLANETS = FALSE) and ($SURROUNDDONTAVOID = FALSE))))
	setvar $SURROUNDAVOIDALLPLANETS TRUE
end
if ($BOT_TEAM_NAME = 0)
	setvar $BOT_TEAM_NAME "misanthrope"
end
if ($PASSWORD = 0)
	setvar $PASSWORD PASSWORD
end
if ($USERNAME = 0)
	setvar $USERNAME LOGINNAME
end
if ($LETTER = 0)
	setvar $LETTER GAME
end
if ($STARDOCK <= 0)
	setvar $STARDOCK STARDOCK
	savevar $STARDOCK
end
if ($RYLOS <= 0)
	setvar $RYLOS RYLOS
	savevar $RYLOS
end
if ($ALPHA_CENTAURI <= 0)
	setvar $ALPHA_CENTAURI ALPHACENTAURI
	savevar $ALPHA_CENTAURI
end
if ($ARMID_COST <= 0)
	setvar $ARMID_COST 1000
end
if ($LIMPET_COST <= 0)
	setvar $LIMPET_COST 40000
end
if ($PHOTON_COST <= 0)
	setvar $PHOTON_COST 100000
end

:RUN_BOT
if ((($STARTINGLOCATION = "Citadel") or ($STARTINGLOCATION = "Command")) and (CONNECTED = TRUE))
	gosub :STARTCNSETTINGS
	gosub :QUIKSTATS
	gosub :GETINFO
	send "'{" $BOT_NAME "} - is ACTIVE: Version - "&$MAJOR_VERSION&"."&$MINOR_VERSION " - type " #34 $BOT_NAME " help" #34 " for command list*"
	send "'{" $BOT_NAME "} - to login - send a corporate memo*"
	if (($USERNAME = "") or ($LETTER = "") or ($DORELOG = FALSE))
		send "'{" $BOT_NAME "} - Auto Relog - Not Active*"
		setvar $DORELOG FALSE
	end
else
	echo "*{" $BOT_NAME "} is ACTIVE: Version - "&$MAJOR_VERSION&"."&$MINOR_VERSION " - type " #34 $BOT_NAME " help" #34 " for command list*"
	if (($USERNAME = "") or ($LETTER = "") or ($DORELOG = FALSE))
		echo "{" $BOT_NAME "} - Auto Relog - Not Active*"
		setvar $DORELOG FALSE
	end
end
savevar $BOT_NAME

:INITIATE_BOT
if (CONNECTED <> TRUE)
	goto :PREGAMEMENULOAD
end
goto :WAIT_FOR_COMMAND

:AUTHORIZE
add $CORPYCOUNT 1
setvar $CORPY[$CORPYCOUNT] $PARM1
fileexists $EXISTS $AUTHORIZED_FILE
if ($EXISTS)
	setvar $I 1
	read $AUTHORIZED_FILE $AUSER $I
	while ($AUSER <> EOF)
		if ($AUSER = $PARM1)
			return
		end
		add $I 1
		read $AUTHORIZED_FILE $AUSER $I
	end
end
write $AUTHORIZED_FILE $PARM1
setvar $MESSAGE $PARM1&" is now authorized to run bot commands.*"
return

:LIST_AUTHORIZED
fileexists $EXISTS $AUTHORIZED_FILE
if ($EXISTS)
	setvar $MESSAGE "Authorized users: "
	setvar $I 1
	read $AUTHORIZED_FILE $AUSER $I
	while ($AUSER <> EOF)
		setvar $MESSAGE $MESSAGE&$AUSER&" "
		add $I 1
		read $AUTHORIZED_FILE $AUSER $I
	end
	setvar $MESSAGE $MESSAGE&"*"
else
	setvar $MESSAGE "No authorized users outside of corp.*"
end
return

:DEAUTHORIZE
setvar $I 1
while ($I < $CORPYCOUNT)
	if ($CORPY[$I] = $PARM1)
		setvar $CORPY[$I] ""
	end
	add $I 1
end
fileexists $EXISTS $AUTHORIZED_FILE
if ($EXISTS)
	readtoarray $AUTHORIZED_FILE $AUTHLIST
	setvar $I 1
	delete $AUTHORIZED_FILE
	while ($I <= $AUTHLIST)
		if ($AUTHLIST[$I] <> $PARM1)
			write $AUTHORIZED_FILE $AUTHLIST[$I]
		end
		add $I 1
	end
end
setvar $MESSAGE $PARM1&" is no longer authorized to run bot commands.*"
return

:HELP_AUTHORIZE
setvar $AUTHHELP "scripts\MomBot\Help\authorize.txt"
fileexists $DOESEXIST $AUTHHELP
if ($DOESEXIST < 1)
	write "scripts\MOMBot\Help\"&$COMMAND&".txt" "- authorize [trader] | list                                 "
	write "scripts\MOMBot\Help\"&$COMMAND&".txt" "                                                            "
	write "scripts\MOMBot\Help\"&$COMMAND&".txt" "  Authorize a trader, not on corp, to issue bot commands    "
	write "scripts\MOMBot\Help\"&$COMMAND&".txt" "  on subspace.                                              "
	write "scripts\MOMBot\Help\"&$COMMAND&".txt" "                                                            "
	write "scripts\MOMBot\Help\"&$COMMAND&".txt" "  >authorize list      List currently authorized traders    "
	write "scripts\MOMBot\Help\"&$COMMAND&".txt" "  >authorize [trader]  Authorize a trader                   "
end
setvar $MESSAGE ""
setvar $I 1
read $AUTHHELP $LINE $I
while ($LINE <> EOF)
	setvar $MESSAGE $MESSAGE&$LINE&"*"
	add $I 1
	read $AUTHHELP $LINE $I
end
return

:HELP_DEAUTHORIZE
setvar $AUTHHELP "scripts\MomBot\Help\deauthorize.txt"
fileexists $DOESEXIST $AUTHHELP
if ($DOESEXIST < 1)
	write "scripts\MOMBot\Help\"&$COMMAND&".txt" "- deauthorize [trader]                                      "
	write "scripts\MOMBot\Help\"&$COMMAND&".txt" "                                                            "
	write "scripts\MOMBot\Help\"&$COMMAND&".txt" "  Deauthorize a trader from issuing bot commands            "
	write "scripts\MOMBot\Help\"&$COMMAND&".txt" "  on subspace.                                              "
	write "scripts\MOMBot\Help\"&$COMMAND&".txt" "                                                            "
	write "scripts\MOMBot\Help\"&$COMMAND&".txt" "  >deauthorize [trader]  Deauthorize a trader               "
end
setvar $MESSAGE ""
setvar $I 1
read $AUTHHELP $LINE $I
while ($LINE <> EOF)
	setvar $MESSAGE $MESSAGE&$LINE&"*"
	add $I 1
	read $AUTHHELP $LINE $I
end
return
