-- The production schema grants dami_app DML on new tables by default. File patch
-- proposals are immutable inputs to approval, so their runtime surface is narrower.

revoke update, delete, truncate, references, trigger
on dami.file_patch_proposals from dami_app;
