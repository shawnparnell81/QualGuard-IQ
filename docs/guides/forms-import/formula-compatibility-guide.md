# Formula compatibility guide

Every spreadsheet formula is classified when a workbook is inspected (formula inventory on the job):

| Class | Meaning |
| --- | --- |
| server | Translated to the form formula language and calculated live on the form |
| volatile | Uses NOW/TODAY/RAND/OFFSET/INDIRECT etc.; flagged for review |
| preserved | Valid Excel function the form engine does not evaluate; the cached value is kept from the file and listed |
| unsupported | Unknown function; listed in the fidelity report |
| external | Refers to another workbook; never followed |
| macro | XLM macro function (CALL, REGISTER, EXEC, EVALUATE, GET.CELL, RUN); never run |
| review | Needs a person to check the translation |

## Functions calculated live
ABS, AND, AVERAGE, AVERAGEIF, AVERAGEIFS, CEILING, CHOOSE, CONCAT, COUNT, COUNTA, COUNTBLANK, COUNTIF, COUNTIFS, CP, CPK, DATE, DATEADD, DAY, DAYS, EDATE, EXACT, FALSE, FLOOR, IF, IFERROR, IFS, INDEX, INT, ISBLANK, ISERROR, ISNUMBER, ISTEXT, LARGE, LEFT, LEN, LOWER, MATCH, MAX, MAXIFS, MEDIAN, MID, MIN, MINIFS, MOD, MONTH, NA, NOT, OR, PI, POWER, PRODUCT, RANGE, RIGHT, ROUND, ROUNDDOWN, ROUNDUP, SIGN, SMALL, SQRT, STDEV, STDEVP, SUBSTITUTE, SUM, SUMIF, SUMIFS, SWITCH, TEXT, TODAY, TRIM, TRUE, TRUNC, UPPER, VALUE, VAR, WITHIN, XOR, YEAR.

Renamed on translation: CONCATENATE→CONCAT, STDEV.S→STDEV, STDEV.P→STDEVP, VAR.S→VAR, CEILING.MATH→CEILING, FLOOR.MATH→FLOOR, IFNA→IFERROR, NOW→TODAY.

## Kept from the file, not evaluated
VLOOKUP, HLOOKUP, XLOOKUP, LOOKUP, OFFSET, INDIRECT, SUMPRODUCT, FILTER, SORT, UNIQUE, SEQUENCE, TRANSPOSE, MMULT, FREQUENCY, TREND, FORECAST, PMT, NPV, IRR, RATE, FV, PV, NORM.DIST/NORMDIST, NORM.INV, NORMSINV, NORM.S.INV, NORM.S.DIST, T.INV, T.INV.2T, TINV, CHIINV, F.INV, PERCENTILE(.INC), QUARTILE, RANK(.EQ), NETWORKDAYS, WORKDAY, DATEDIF, WEEKNUM, EOMONTH, HOUR, MINUTE, SECOND, TIME, HYPERLINK, ADDRESS, ROW, COLUMN, ROWS, COLUMNS, TEXTJOIN, LET, LAMBDA.

Cell references become field keys (for example `=IF(B4>500,32,...)` becomes `IF(lot_qty>500,32,...)`). Formulas are validated with empty input when the template is saved.
