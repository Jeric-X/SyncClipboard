#!/bin/bash

CHANGES=../src/SyncClipboard.Desktop/Changes.md

cat $CHANGES | head -n 1 | tr -d "\n" > version.txt
awk '{if ($0 == "") exit; else print}' $CHANGES > featuretemp.txt
tail -n +2 featuretemp.txt > feature.txt
