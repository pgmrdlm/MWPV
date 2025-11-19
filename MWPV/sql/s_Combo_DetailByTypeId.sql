SELECT
  cd.ComboDetailId AS ComboDet,
  cd.ComboTypeId   AS ComboTyp,
  cd.Seq,
  cd.Code,
  cd.Description,
  cd.Active,
  cd.CreatedUtc,
  cd.UpdatedUtc
FROM ComboDetail cd
WHERE cd.ComboTypeId = @ComboTypeId
  AND cd.Active = 1
ORDER BY cd.Seq;