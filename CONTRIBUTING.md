==========================================================
GitHub Branching Model
==========================================================
----------------------------------------------------------
Main Branches
----------------------------------------------------------

Never commit directly to these branches!

Master: This branch matches the current version on the asset store.

Develop: This branch is where new feature branches are merged in to as they are completed.

----------------------------------------------------------
Supporting Branches
----------------------------------------------------------

Release(version#)

May branch from:	develop

Must merge back into:	develop and master

Branch naming convention:	release-*

----------------------------------------------------------
Feature

May branch from:	develop

Must merge back into:	develop

Branch naming convention:	feature-*

----------------------------------------------------------
Hotfix

May branch from:	master

Must merge back into:	develop and master

Branch naming convention:	hotfix-*
