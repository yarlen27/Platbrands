-- Migración para agregar campo vector_store_file_id a la tabla proceso_ocr
-- Ejecutar este script en la base de datos

ALTER TABLE public.proceso_ocr 
ADD COLUMN IF NOT EXISTS vector_store_file_id TEXT;