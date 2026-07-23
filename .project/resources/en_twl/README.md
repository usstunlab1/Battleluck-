# en_twl

Links from the original Bible languages to Translation Words.

Previously the links in UHB and UGNT were used (but that didn't enable the links to be customized for Gateway Languages).

## Editing the TWL

To edit the TWL files there are three options:

* Use LibreOffice (Recommended)
* Use a text editor on your computer
* Use the online web editor in DCS

Each of these options and their caveats are described below.

The first two options require you to clone the repository to your computer first. You may do this on the command line or using a program such as SmartGit. After making changes to the files you will need to commit and push your changes to the server and then create a Pull Request to merge them to the `master` branch.

Alternately, you may [download the master branch as a zip file](https://git.door43.org/unfoldingWord/en_twl/archive/master.zip) and extract that locally. After editing you would need to use the upload file feature in DCS to get your changes ready for a Pull Request.

### Editing in LibreOffice

This is the recommended way to edit the TSV files. You may [download LibreOffice](https://www.libreoffice.org/download/download/) for free.

After you have the file on your computer, you may open the respective TSV file with LibreOffice. Follow these notes on the Text Import Screen:

* Set “Separated by” to “Tab”
* Set “Text Delimiter” to blank, you will need to highlight the character and use backspace or delete to remove it

It should look like this:

![Screenshot of LibreOffice Text Import dialog](https://cdn.door43.org/assets/img/twl/LibreOfficeTextImport.png)

When you are done editing, click Save and then select “Use Text CSV Format” on the pop up dialogue. Note that even though it says CSV, it will use tab characters as the field separators.

**Note:** Other spreadsheet editors **should not** be used because they will add or remove quotation marks which will affect the entries negatively.

### Editing in a Text Editor

You may also use a regular text editor to make changes to the files.

**Note:** You must be careful not to delete or add any tab characters when editing with this method.

### Editing in DCS

If you only need to change a word or two, this may be the quickest way to make your change. See the [protected branch workflow](https://help.door43.org/en/knowledgebase/15-door43-content-service/docs/46-protected-branch-workflow) document for step by step instructions.

**Note:** You must be careful not to delete any tab characters when editing with this method.

## Structure

The TWL are structured as TSV files to simplify importing and exporting into various formats for translation and presentation. The TWLs are keyed to the original Greek and Hebrew text, but clones of this repository can be made for the Gateway Languages (GLs), and then the TW links can be adjusted to accommodate the workings of those languages.

### TSV Format Overview

A Tab Separated Value (TSV) file is like a Comma Separated Value file except that the tab character is what divides the values instead of a comma. This makes it easier to include prose text in the files because many languages require the use of commas, single quotes, and double quotes in their sentences and paragraphs.

The TWL are structured as one file per book of the Bible and encoded in TSV format, for example, `twl_GEN.tsv`. The six columns are `Reference`, `ID`, `Tags`, `OrigWords`, `Occurrence`, and `TWLink`.

### TWL TSV Column Description

The following lists each column with a brief description and example.

* `Reference`: Chapter number (e.g. `1`) then colon then verse number (e.g. `3`) or `intro`
* `ID`: Four character **alphanumeric** string unique *within* the verse for the resource (e.g. `swi9`)
  * This will be helpful in identifing which links came from the English resources and which links have been added by GLs.
  * The Universal ID (UID) of a note is the combination of the `Book`, `Chapter`, `Verse`, and `ID` values. For example, `tit/1/3/swi9`.
    * This is a useful way to unambiguously refer to links.
    * An [RC link](https://resource-container.readthedocs.io/en/latest/linking.html) can resolve to a specific note like this: `rc://en/tn/help/tit/01/01/swi9`.
* `Tags`: (optional) any of `keyterm` or `name`, separated by `; ` if there's more than one
* `OrigWords`: Original language quote (e.g. `ἐφανέρωσεν↔τὸν λόγον αὐτοῦ`) but most often just one word
* `Occurrence`: Specifies which occurrence in the original language text the entry applies to.
  * `-1`: entry applies to every occurrence of OrigWords in the verse
  * `0`: entry does not occur in original language (for example, “Connecting Statement:”)
  * `1`: entry applies to first occurrence of OrigWords only
  * `2`: entry applies to second occurrence of OrigWords only
  * etc.
* `TWLink`: A Resource Container link to a TranslationWords article, e.g., rc://*/tw/dict/bible/other/peace
