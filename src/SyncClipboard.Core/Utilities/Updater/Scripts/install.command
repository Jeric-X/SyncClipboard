#!/bin/sh
# Terminal opens this executable script; paths remain data, including spaces and apostrophes.
work=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
exec /bin/sh "$work/install.sh" "$work" supervisor
