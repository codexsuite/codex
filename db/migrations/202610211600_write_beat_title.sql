-- #47: the Scene Beat card's optional bold title (design/screens/write.html `.bt`). Empty means no title.
ALTER TABLE beat ADD COLUMN title text NOT NULL DEFAULT '';
