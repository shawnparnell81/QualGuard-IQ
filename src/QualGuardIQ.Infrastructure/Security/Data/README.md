# Common password list

`common-passwords.txt.gz` holds the lower-cased entries of 8 characters or more from the UK National Cyber Security
Centre list of the 100,000 passwords seen most often in breach data (published through Have I Been Pwned), as
distributed in SecLists (`Passwords/Common-Credentials/100k-most-used-passwords-NCSC.txt`, MIT licence).

The check runs offline: new passwords are compared against this list, and against the list after trailing digits and
symbols are removed, so `Password2024!` is refused as well as `password`. Nothing is sent to an outside service.
