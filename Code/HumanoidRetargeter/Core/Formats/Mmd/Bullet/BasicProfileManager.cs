/*
 * C# / XNA  port of Bullet (c) 2011 Mark Neale <xexuxjy@hotmail.com>
 *
 * Bullet Continuous Collision Detection and Physics Library
 * Copyright (c) 2003-2008 Erwin Coumans  http://www.bulletphysics.com/
 *
 * This software is provided 'as-is', without any express or implied warranty.
 * In no event will the authors be held liable for any damages arising from
 * the use of this software.
 *
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 *
 * 1. The origin of this software must not be misrepresented; you must not
 *    claim that you wrote the original software. If you use this software
 *    in a product, an acknowledgment in the product documentation would be
 *    appreciated but is not required.
 * 2. Altered source versions must be plainly marked as such, and must not be
 *    misrepresented as being the original software.
 * 3. This notice may not be removed or altered from any source distribution.
 */
// Adapted from BulletXNA 73b2e1b9: namespace isolation, no XNA rendering, one type per file.
#nullable disable
#pragma warning disable CS1591, CS0168, CS0219, CS0414, CS0660, CS0661, CS0659, CS0693, CS8981, CS0108, CS0114
using System;
using System.Diagnostics;

namespace HumanoidRetargeter.Core.Formats.Mmd.Bullet;

internal class BasicProfileManager : IProfileManager
{
	public BasicProfileManager()
	{
		m_root = new CProfileNode("Root", null, this);
		m_currentNode = m_root;
		m_stopwatch = new Stopwatch();
	}

	public void Start_Profile(string name)
	{
		if (name != m_currentNode.Get_Name())
		{
			m_currentNode = m_currentNode.Get_Sub_Node(name);
		}

		m_currentNode.Call();
	}

	public void Stop_Profile()
	{
		// Return will indicate whether we should back up to our parent (we may
		// be profiling a recursive function)
		if (m_currentNode.Return())
		{
			m_currentNode = m_currentNode.Get_Parent();
		}
	}

	public void CleanupMemory()
	{
		throw new NotImplementedException();
	}

	public void Reset()
	{
		m_stopwatch.Reset();
		m_stopwatch.Start();
		m_root.Reset();
		m_root.Call();
		m_frameCounter = 0;
		m_resetTime = m_stopwatch.ElapsedTicks;
	}

	public void Increment_Frame_Counter()
	{
		m_frameCounter++;
	}

	public int Get_Frame_Count_Since_Reset()
	{
		long time = m_stopwatch.ElapsedTicks;
		time -= m_resetTime;
		return (int)((float)time / Profile_Get_Tick_Rate());
	}

	public float Profile_Get_Tick_Rate()
	{
		return 1000.0f;
	}

	public float Get_Time_Since_Reset()
	{
		long time = m_stopwatch.ElapsedTicks;
		time -= m_resetTime;
		return (float)time / Profile_Get_Tick_Rate();
	}

	public void DumpRecursive(IProfileIterator profileIterator, int spacing)
	{
	//profileIterator.First();
	//if (profileIterator.Is_Done())
	//    return;
	//float accumulated_time=0,parent_time = profileIterator.Is_Root() ? CProfileManager::Get_Time_Since_Reset() : profileIterator.Get_Current_Parent_Total_Time();
	//int i;
	//int frames_since_reset = CProfileManager::Get_Frame_Count_Since_Reset();
	//for (i=0;i<spacing;i++)	printf(".");
	//printf("----------------------------------\n");
	//for (i=0;i<spacing;i++)	printf(".");
	//printf("Profiling: %s (total running time: %.3f ms) ---\n",	profileIterator.Get_Current_Parent_Name(), parent_time );
	//float totalTime = 0.f;
	//int numChildren = 0;
	//for (i = 0; !profileIterator.Is_Done(); i++,profileIterator.Next())
	//{
	//    numChildren++;
	//    float current_total_time = profileIterator.Get_Current_Total_Time();
	//    accumulated_time += current_total_time;
	//    float fraction = parent_time > SIMD_EPSILON ? (current_total_time / parent_time) * 100 : 0.f;
	//    {
	//        int i;	for (i=0;i<spacing;i++)	printf(".");
	//    }
	//    printf("%d -- %s (%.2f %%) :: %.3f ms / frame (%d calls)\n",i, profileIterator.Get_Current_Name(), fraction,(current_total_time / (double)frames_since_reset),profileIterator.Get_Current_Total_Calls());
	//    totalTime += current_total_time;
	//    //recurse into children
	//}
	//if (parent_time < accumulated_time)
	//{
	//    printf("what's wrong\n");
	//}
	//for (i=0;i<spacing;i++)	printf(".");
	//printf("%s (%.3f %%) :: %.3f ms\n", "Unaccounted:",parent_time > SIMD_EPSILON ? ((parent_time - accumulated_time) / parent_time) * 100 : 0.f, parent_time - accumulated_time);
	//for (i=0;i<numChildren;i++)
	//{
	//    profileIterator.Enter_Child(i);
	//    dumpRecursive(profileIterator,spacing+3);
	//    profileIterator.Enter_Parent();
	//}
	}

	public void DumpAll()
	{
	//CProfileIterator* profileIterator = 0;
	//profileIterator = CProfileManager::Get_Iterator();
	//dumpRecursive(profileIterator,0);
	//CProfileManager::Release_Iterator(profileIterator);
	}

	public IProfileIterator getIterator()
	{
		return new BasicProfileIterator(m_root);
	}

	CProfileNode m_root;
	CProfileNode m_currentNode;
	int m_frameCounter = 0;
	long m_resetTime = 0;
	public Stopwatch m_stopwatch;
}