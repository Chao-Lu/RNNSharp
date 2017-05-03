﻿using AdvUtils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace RNNSharp
{
    public class RNNHelper
    {
        public static Vector<float> vecMaxGrad;
        public static Vector<float> vecMinGrad;
        public static Vector<float> vecNormalLearningRate;
        public static Random rand = new Random(DateTime.Now.Millisecond);
        public static float GradientCutoff { get; set; }
        public static float LearningRate { get; set; }
        public static bool IsConstAlpha { get; set; }

        public static int MiniBatchSize { get; set; }

        public static float random(float min, float max)
        {
            return (float)(rand.NextDouble() * (max - min) + min);
        }

        public static float RandInitWeight()
        {
            return random(-0.1f, 0.1f) + random(-0.1f, 0.1f) + random(-0.1f, 0.1f);
        }

        public static float NormalizeGradient(float err)
        {
            if (err > GradientCutoff)
            {
                err = GradientCutoff;
            }
            else if (err < -GradientCutoff)
            {
                err = -GradientCutoff;
            }
            return err;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<float> NormalizeGradient(Vector<float> v)
        {
            v = Vector.Min(v, vecMaxGrad);
            v = Vector.Max(v, vecMinGrad);

            return v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeLearningRate(Matrix<float> m, int i, int j, float delta)
        {
            if (IsConstAlpha)
            {
                return LearningRate;
            }
            var dg = m[i][j] + delta * delta;
            m[i][j] = dg;

            return (float)(LearningRate / (1.0 + Math.Sqrt(dg)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<float> ComputeLearningRate(Vector<float> vecDelta, ref Vector<float> vecWeightLearningRate)
        {
            if (RNNHelper.IsConstAlpha)
            {
                return RNNHelper.vecNormalLearningRate;
            }

            vecWeightLearningRate += vecDelta * vecDelta;
            return vecNormalLearningRate / (Vector.SquareRoot(vecWeightLearningRate) + Vector<float>.One);

        }

        //Save matrix into file as binary format
        public static void SaveMatrix(Matrix<float> mat, BinaryWriter fo, bool bVQ = false)
        {
            //Save the width and height of the matrix
            fo.Write(mat.Width);
            fo.Write(mat.Height);

            if (bVQ == false)
            {
                Logger.WriteLine("Saving matrix without VQ...");
                fo.Write(0); // non-VQ

                //Save the data in matrix
                for (var r = 0; r < mat.Height; r++)
                {
                    for (var c = 0; c < mat.Width; c++)
                    {
                        fo.Write(mat[r][c]);
                    }
                }
            }
            else
            {
                //Build vector quantization matrix
                var vqSize = 256;
                var vq = new VectorQuantization();
                Logger.WriteLine("Saving matrix with VQ {0}...", vqSize);

                var valSize = 0;
                for (var i = 0; i < mat.Height; i++)
                {
                    for (var j = 0; j < mat.Width; j++)
                    {
                        vq.Add(mat[i][j]);
                        valSize++;
                    }
                }

                if (vqSize > valSize)
                {
                    vqSize = valSize;
                }

                var distortion = vq.BuildCodebook(vqSize);
                Logger.WriteLine("Distortion: {0}, vqSize: {1}", distortion, vqSize);

                //Save VQ codebook into file
                fo.Write(vqSize);
                for (var j = 0; j < vqSize; j++)
                {
                    fo.Write(vq.CodeBook[j]);
                }

                //Save the data in matrix
                for (var r = 0; r < mat.Height; r++)
                {
                    for (var c = 0; c < mat.Width; c++)
                    {
                        fo.Write((byte)vq.ComputeVQ(mat[r][c]));
                    }
                }
            }
        }

        public static void matrixXvectorADD(float[] dest, float[] srcvec, Matrix<float> srcmatrix, int DestSize,
            int SrcSize)
        {
            for (var i = 0; i < DestSize; i++)
            {
                var vector_i = srcmatrix[i];
                float cellOutput = 0;
                var j = 0;

                var moreItems = (SrcSize % Vector<float>.Count);
                while (j < SrcSize - moreItems)
                {
                    var v1 = new Vector<float>(srcvec, j);
                    var v2 = new Vector<float>(vector_i, j);
                    cellOutput += Vector.Dot(v1, v2);

                    j += Vector<float>.Count;
                }

                while (j < SrcSize)
                {
                    cellOutput += srcvec[j] * vector_i[j];
                    j++;
                }

                dest[i] = cellOutput;
            }
        }

        public static Matrix<float> LoadMatrix(BinaryReader br)
        {
            var width = br.ReadInt32();
            var height = br.ReadInt32();
            var vqSize = br.ReadInt32();
            Logger.WriteLine("Loading matrix. width: {0}, height: {1}, vqSize: {2}", width, height, vqSize);

            var m = new Matrix<float>(height, width);
            if (vqSize == 0)
            {
                for (var r = 0; r < height; r++)
                {
                    for (var c = 0; c < width; c++)
                    {
                        m[r][c] = br.ReadSingle();
                    }
                }
            }
            else
            {
                var codeBook = new List<float>();

                for (var i = 0; i < vqSize; i++)
                {
                    codeBook.Add(br.ReadSingle());
                }

                for (var r = 0; r < height; r++)
                {
                    for (var c = 0; c < width; c++)
                    {
                        int vqIndex = br.ReadByte();
                        m[r][c] = codeBook[vqIndex];
                    }
                }
            }

            return m;
        }

        public static void matrixXvectorADD(float[] dest, float[] srcvec, Matrix<float> srcmatrix,
            HashSet<int> setSkipSampling, int SrcSize)
        {

            foreach (var i in setSkipSampling)
            {
                float cellOutput = 0;
                var vector_i = srcmatrix[i];
                var j = 0;

                var moreItems = (SrcSize % Vector<float>.Count);
                while (j < SrcSize - moreItems)
                {
                    var v1 = new Vector<float>(srcvec, j);
                    var v2 = new Vector<float>(vector_i, j);
                    cellOutput += Vector.Dot(v1, v2);

                    j += Vector<float>.Count;
                }

                while (j < SrcSize)
                {
                    cellOutput += srcvec[j] * vector_i[j];
                    j++;
                }

                dest[i] = cellOutput;
            }
        }

        public static void matrixXvectorADDErr(float[] dest, float[] srcvec, Matrix<float> srcmatrix, int DestSize,
           int SrcSize)
        {
            Array.Clear(dest, 0, dest.Length);
            for (var j = 0; j < SrcSize; j++)
            {
                int i = 0;
                float src = srcvec[j];
                float[] srcVector = srcmatrix[j];

                var moreItems = (DestSize % Vector<float>.Count);
                while (i < DestSize - moreItems)
                {
                    Vector<float> vecSrc = new Vector<float>(srcVector, i);
                    Vector<float> vecDest = new Vector<float>(dest, i);
                    vecDest += src * vecSrc;

                    vecDest.CopyTo(dest, i);
                    i += Vector<float>.Count;
                }

                while (i < DestSize)
                {
                    dest[i] += src * srcVector[i];
                    i++;
                }
            }
        }

        public static void matrixXvectorADDErr(float[] dest, float[] srcvec, Matrix<float> srcmatrix, int DestSize,
                    HashSet<int> setSkipSampling)
        {
            Array.Clear(dest, 0, dest.Length);
            int cnt = 0;
            foreach (int j in setSkipSampling)
            {
                int i = 0;
                float src = srcvec[j];
                float[] srcVector = srcmatrix[j];

                var moreItems = (DestSize % Vector<float>.Count);
                while (i < DestSize - moreItems)
                {
                    Vector<float> vecSrc = new Vector<float>(srcVector, i);
                    Vector<float> vecDest = new Vector<float>(dest, i);
                    vecDest += src * vecSrc;

                    vecDest.CopyTo(dest, i);
                    i += Vector<float>.Count;
                }

                while (i < DestSize)
                {
                    dest[i] += src * srcVector[i];
                    i++;
                }

                cnt++;
            }
        }
    }
}